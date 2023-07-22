use axum::{body, extract, http, Json, response, Router, routing};
use std::collections::BTreeMap;

mod db;
mod view;

#[derive(Clone)]
struct AppState<V: db::Event> {
	dbs: BTreeMap<String, db::Mem<V>>
}

impl<V: db::Event> AppState<V> {
	fn new() -> Self {
		Self { dbs: BTreeMap::new() }
	}
}

async fn create_db<V: db::Event>(
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

async fn bulk_read<V: db::Event + serde::Serialize>(
	extract::Query(db_name): extract::Query<String>,
	extract::Query(BulkRead {keys}): extract::Query<BulkRead>,
	extract::State(state): extract::State<AppState<V>>
) -> Result<axum::Json<Vec<V>>, http::StatusCode> {
    let db = state.dbs.get(&db_name).ok_or(http::StatusCode::NOT_FOUND)?;
    let events = db.get(&keys).cloned();
    Ok(Json(events.collect()))
}

async fn bulk_write<V: db::Event + serde::Serialize + for<'a> serde::Deserialize<'a>>(
	extract::State(mut state): extract::State<AppState<V>>,
	extract::Query(db_name): extract::Query<String>,
    Json(values): Json<Vec<V>>
) -> Result<axum::Json<Vec<db::Key>>, http::StatusCode> {
    let db = state.dbs.get_mut(&db_name).ok_or(http::StatusCode::NOT_FOUND)?;
    let new_keys = db.add_local(&values);
    Ok(Json(new_keys))
}

pub fn app<V: db::Event + serde::Serialize + 'static + for<'a> serde::Deserialize<'a>>() -> Router {
	Router::new()
		 .route("/db/:name", routing::post(create_db::<V>))
		 .route("/db/:name/e/:args", routing::get(bulk_read::<V>))
		 .route("/db/:name/e", routing::post(bulk_write::<V>))
         .with_state(AppState::new())
}

#[cfg(test)]
mod tests {
	use super::*;
	use pretty_assertions::assert_eq;
	
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
	}
}

