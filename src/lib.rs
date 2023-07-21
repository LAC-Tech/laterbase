use axum::{body, extract, http, response, Router, routing};
use std::collections::{BTreeMap};

mod db;

type ViewData = BTreeMap<Vec<u8>, Vec<u8>>; 
type ViewFn = fn(&ViewData, &[u8]) -> ViewData;

#[derive(Clone)]
struct AppState<V: db::Val> {
	dbs: BTreeMap<String, db::Mem<V>>
}

impl<V: db::Val> AppState<V> {
	fn new() -> Self {
		Self { dbs: BTreeMap::new() }
	}
}

async fn create_db<V: db::Val>(
	extract::Path(name): extract::Path<String>,
	extract::State(mut state): extract::State<AppState<V>>
) -> impl response::IntoResponse {
	state.dbs.insert(name, db::Mem::new());

    http::StatusCode::CREATED
}

#[derive(serde::Deserialize)]
struct BulkRead {
	keys: Vec<db::Key>
}

async fn bulk_read<V: db::Val + serde::Serialize>(
	extract::Query(db_name): extract::Query<String>,
	extract::Query(BulkRead {keys}): extract::Query<BulkRead>,
	extract::State(state): extract::State<AppState<V>>
) -> Result<axum::Json<Vec<V>>, http::StatusCode> {
    let db = state.dbs.get(&db_name).ok_or(http::StatusCode::NOT_FOUND)?;
    let events =db.get(&keys).cloned();
    Ok(axum::Json(events.collect()))
}

fn app<V: db::Val + 'static>() -> Router {
	Router::new()
		 .route("/db/:name", routing::post(create_db::<V>))
		 .with_state(AppState::new())
}

#[cfg(test)]
mod tests {
	use super::*;
	use pretty_assertions::{assert_eq};
	
	use tower::Service; // for `call`
	use tower::ServiceExt; // for `oneshot` and `ready`

	#[tokio::test]
	async fn can_create_db() {
		let mut app = app::<i32>();

		let req = http::Request::builder()
			.method("POST")
			.uri("/db/:name")
			.body(body::Body::empty())
			.unwrap();
		let res = app.ready().await.unwrap().call(req).await.unwrap();
		assert_eq!(res.status(), http::StatusCode::CREATED);

		let req = http::Request::builder()
			.method("GET")
			.uri("/db/:name")
			.body(body::Body::empty())
			.unwrap();
		let res = app.ready().await.unwrap().call(req).await.unwrap();
		assert_eq!(res.status(), http::StatusCode::OK);
	}
}

