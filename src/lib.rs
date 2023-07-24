use std::collections::BTreeMap;

use serde::{de, Serialize};
use axum::{extract, http, Json, response, Router, routing};

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

async fn db_info<E: db::Event>(
	extract::Path(db_name): extract::Path<String>,
	extract::State(state): extract::State<AppState<E>>
) -> Result<(http::StatusCode, axum::Json<db::Info>), http::StatusCode>  {
	let db = state.dbs.get(&db_name).ok_or(http::StatusCode::NOT_FOUND)?;
	Ok((http::StatusCode::CREATED, Json(db.info())))
}

async fn bulk_read<E: db::Event + Serialize>(
	extract::Query(db_name): extract::Query<String>,
	extract::Query(BulkRead {keys}): extract::Query<BulkRead>,
	extract::State(state): extract::State<AppState<E>>
) -> Result<axum::Json<Vec<E>>, http::StatusCode> {
    let db = state.dbs.get(&db_name).ok_or(http::StatusCode::NOT_FOUND)?;
    let events = db.get(&keys).cloned();
    Ok(Json(events.collect()))
}

async fn bulk_write<V: db::Event + Serialize + de::DeserializeOwned>(
	extract::State(mut state): extract::State<AppState<V>>,
	extract::Query(db_name): extract::Query<String>,
    Json(values): Json<Vec<V>>
) -> Result<axum::Json<Vec<db::Key>>, http::StatusCode> {
    let db = state.dbs.get_mut(&db_name).ok_or(http::StatusCode::NOT_FOUND)?;
    let new_keys = db.add_local(&values);
    Ok(Json(new_keys))
}

pub fn app<V: db::Event + Serialize + 'static + de::DeserializeOwned>() -> Router {
	Router::new()
		 .route("/db/:name", routing::post(create_db::<V>))
		 .route("/db/:name", routing::get(db_info::<V>))
		 .route("/db/:name/e/:args", routing::get(bulk_read::<V>))
		 .route("/db/:name/e", routing::post(bulk_write::<V>))
         .with_state(AppState::new())
}

#[cfg(test)]
mod tests {
	use super::*;
	use pretty_assertions::{assert_eq, assert_ne};
	
	use tower::Service; // for `call`
	use tower::ServiceExt; // for `oneshot` and `ready`
	
	use axum::body;

	#[tokio::test]
	async fn can_create_db() {
		let mut app = app::<i32>();
		
		let db_name = "test";

		let req = http::Request::builder()
			.method("POST")
			.uri(format!("/db/{db_name}"))
			.body(body::Body::empty())
			.unwrap();
		let res = app.ready().await.unwrap().call(req).await.unwrap();
		assert_eq!(res.status(), http::StatusCode::CREATED);

		let req = http::Request::builder()
			.method("GET")
			.uri(format!("/db/{db_name}"))
			.body(body::Body::empty())
			.unwrap();
		
		let res = app.ready().await.unwrap().call(req).await.unwrap();
		let status = &res.status();

		let body_bytes = hyper::body::to_bytes(res.into_body()).await.unwrap();
		assert_eq!(status, &http::StatusCode::OK);
		assert_ne!(body_bytes.len(), 0);
	}
}

