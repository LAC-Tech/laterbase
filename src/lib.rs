use std::collections::BTreeMap;
use std::sync::{Arc, RwLock, RwLockReadGuard, RwLockWriteGuard};

use serde::{de, Serialize};
use axum::{http, Json, response, Router, routing};
use axum::extract::{Path, State, Query};

mod db;
mod view;

#[derive(Clone)]
struct AppState<V: db::Event> {
	dbs: Arc<RwLock<BTreeMap<String, db::Mem<V>>>>
}

impl<V: db::Event> AppState<V> {
	fn new() -> Self {
		Self { dbs: Arc::new(RwLock::new(BTreeMap::new())) }
	}

	fn read_dbs(&self) -> RwLockReadGuard<BTreeMap<String, db::Mem<V>>> {
		self.dbs.read().unwrap()
	}

	fn write_dbs(&self) -> RwLockWriteGuard<BTreeMap<String, db::Mem<V>>> {
		self.dbs.write().unwrap()
	}
}

async fn create_db<V: db::Event>(
	Path(name): Path<String>,
	State(state): State<AppState<V>>
) -> impl response::IntoResponse {
	let mut dbs = state.write_dbs();
	dbs.insert(name, db::Mem::new());
    http::StatusCode::CREATED
}

#[derive(serde::Deserialize)]
struct BulkRead {
	keys: Vec<db::Key>
}

async fn db_info<E: db::Event>(
	Path(db_name): Path<String>,
	State(state): State<AppState<E>>
) -> Result<(http::StatusCode, axum::Json<db::Info>), http::StatusCode>  {
	let dbs = state.read_dbs();
	let db = dbs.get(&db_name).ok_or(http::StatusCode::NOT_FOUND)?;
	Ok((http::StatusCode::OK, Json(db.info())))
}

async fn bulk_read<E: db::Event + Serialize>(
	Query(db_name): Query<String>,
	Query(BulkRead {keys}): Query<BulkRead>,
	State(state): State<AppState<E>>
) -> Result<axum::Json<Vec<E>>, http::StatusCode> {
	let dbs = state.read_dbs();
    let db = dbs.get(&db_name).ok_or(http::StatusCode::NOT_FOUND)?;
    let events = db.get(&keys).cloned();
    Ok(Json(events.collect()))
}

async fn bulk_write<V: db::Event + Serialize + de::DeserializeOwned>(
	State(state): State<AppState<V>>,
	Query(db_name): Query<String>,
    Json(values): Json<Vec<V>>
) -> Result<axum::Json<Vec<db::Key>>, http::StatusCode> {
    println!("\nhere\n");
	let mut dbs = state.write_dbs();
	let db = dbs.get_mut(&db_name).ok_or(http::StatusCode::NOT_FOUND)?;
    let new_keys = db.add_local(&values);
    Ok(Json(new_keys))
}

pub fn app<V: db::Event + Serialize + 'static + de::DeserializeOwned>() -> Router {
	Router::new()
		 .route("/db/:name", routing::post(create_db::<V>))
		 .route("/db/:name", routing::get(db_info::<V>))
		 .route("/db/:name/e", routing::put(bulk_write::<V>))
		 .route("/db/:name/e/:args", routing::get(bulk_read::<V>))
         .with_state(AppState::new())
}

#[cfg(test)]
mod tests {
	use super::*;
	use pretty_assertions::assert_eq;
	
	use tower::Service; // for `call`
	use tower::ServiceExt; // for `oneshot` and `ready`
	
	use axum::body;
	use axum::http::StatusCode;

	// Is the cure worse than the disease?
	async fn result(
		app: &mut axum::Router, 
		req: http::Request<axum::body::Body>
	) -> http::Response<axum::body::BoxBody> {
		app.ready().await.unwrap().call(req).await.unwrap()
	}

	#[tokio::test]
	async fn can_create_db() {
		let mut app = app::<i32>();
		let db_name = "test"; // TODO: arbitrary

		/*
		 * Create a database
		 */
		let req = http::Request::builder()
			.method("POST")
			.uri(format!("/db/{db_name}")) .body(body::Body::empty())
			.unwrap();
		let res = result(&mut app, req).await;
		assert_eq!(res.status(), StatusCode::CREATED);

		/*
		 * Confirm database has been created and is empty
		 */
		let req = http::Request::builder()
			.method("GET")
			.uri(format!("/db/{db_name}"))
			.body(body::Body::empty())
			.unwrap();
		let res = result(&mut app, req).await;
		let status = &res.status();

		let body_bytes = hyper::body::to_bytes(res.into_body()).await.unwrap();
		let actual: serde_json::Value = 
			serde_json::from_slice(&body_bytes).unwrap();

		let expected = serde_json::json!({
			"storage_engine": "memory",
			"n_events": 0
		});
		assert_eq!(status, &StatusCode::OK);
		assert_eq!(actual, expected);

		/*
		 * Write events to db
		 */
		let events = vec![1, 2, 3];
		
		let req = http::Request::builder()
			.method("PUT")
			.uri(format!("/db/{db_name}/e"))
			.body(body::Body::from(
				serde_json::to_vec(&events).unwrap()
			))
			.unwrap();

		let res = result(&mut app, req).await;
		let status = &res.status();
		
		let body_bytes = hyper::body::to_bytes(res.into_body()).await.unwrap();
        
        let actual = String::from_utf8(body_bytes.into()).unwrap();
        assert_eq!(actual, "lol");
        //assert_eq!(status, &StatusCode::CREATED);
	}
}

