use std::collections::BTreeMap;
use std::sync::{Arc, RwLock, RwLockReadGuard, RwLockWriteGuard};

use axum::extract::{Path, Query, State};
use axum::{http, response, routing, Json, Router};
use serde::{de, Serialize};

mod db;
mod view;

#[derive(Clone)]
struct AppState<V: db::Event> {
	dbs: Arc<RwLock<BTreeMap<String, db::Mem<V>>>>,
}

impl<V: db::Event> AppState<V> {
	fn new() -> Self {
		Self {
			dbs: Arc::new(RwLock::new(BTreeMap::new())),
		}
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
	State(state): State<AppState<V>>,
) -> impl response::IntoResponse {
	let mut dbs = state.write_dbs();
	dbs.insert(name, db::Mem::new());
	http::StatusCode::CREATED
}

#[derive(serde::Deserialize)]
struct BulkRead {
	keys: Vec<db::Key>,
}

async fn db_info<E: db::Event>(
	Path(db_name): Path<String>,
	State(state): State<AppState<E>>,
) -> Result<(http::StatusCode, axum::Json<db::Info>), http::StatusCode> {
	let dbs = state.read_dbs();
	let db = dbs.get(&db_name).ok_or(http::StatusCode::NOT_FOUND)?;
	Ok((http::StatusCode::OK, Json(db.info())))
}

async fn bulk_read<E: db::Event + Serialize>(
	Query(db_name): Query<String>,
	Query(BulkRead { keys }): Query<BulkRead>,
	State(state): State<AppState<E>>,
) -> Result<axum::Json<Vec<E>>, http::StatusCode> {
	let dbs = state.read_dbs();
	let db = dbs.get(&db_name).ok_or(http::StatusCode::NOT_FOUND)?;
	let events = db.get(&keys).cloned();
	Ok(Json(events.collect()))
}

async fn bulk_write<V: db::Event + Serialize + de::DeserializeOwned>(
	State(state): State<AppState<V>>,
	Path(db_name): Path<String>,
	Json(values): Json<Vec<V>>,
) -> Result<(http::StatusCode, axum::Json<Vec<String>>), http::StatusCode> {
	println!("\nhere\n");
	let mut dbs = state.write_dbs();
	let db = dbs.get_mut(&db_name).ok_or(http::StatusCode::NOT_FOUND)?;
	let new_keys = db
		.add_local(&values)
		.iter()
		.map(|k| k.to_string())
		.collect();
	Ok((http::StatusCode::CREATED, Json(new_keys)))
}

pub fn router<V: db::Event + Serialize + 'static + de::DeserializeOwned>() -> Router {
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

	use axum::body::{Body, BoxBody};
	use axum::http::StatusCode;

	use proptest::prelude::*;
	use std::collections::HashSet;

	async fn result(app: &mut axum::Router, req: http::Request<Body>) -> http::Response<BoxBody> {
		app.ready().await.unwrap().call(req).await.unwrap()
	}

	async fn body<T: de::DeserializeOwned>(res: http::Response<BoxBody>) -> T {
		let reader = hyper::body::to_bytes(res.into_body())
			.await
			.map(std::io::Cursor::new)
			.unwrap();

		serde_json::from_reader(reader).unwrap()
	}

	fn url_safe_string() -> impl Strategy<Value = String> {
		proptest::string::string_regex("[A-Za-z0-9\\-_\\.~]+").unwrap()
	}

	fn random_int_array() -> impl Strategy<Value = Vec<i32>> {
		proptest::collection::vec(proptest::num::i32::ANY, 0..=100)
	}

	use test_strategy::proptest;

	#[proptest(async = "tokio")]
	async fn basic_crud(#[strategy(url_safe_string())] db_name: String) {
		let mut app = router::<i32>();

		// Create a database
		{
			let req = http::Request::builder()
				.method("POST")
				.uri(format!("/db/{db_name}"))
				.body(Body::empty())
				.unwrap();

			let res = result(&mut app, req).await;
			assert_eq!(res.status(), StatusCode::CREATED);
		}

		// Confirm database has been created and is empty
		{
			let req = http::Request::builder()
				.method("GET")
				.uri(format!("/db/{db_name}"))
				.body(Body::empty())
				.unwrap();
			let res = result(&mut app, req).await;
			let status = res.status().clone();

			let actual: db::Info = body(res).await;

			let expected = db::Info {
				storage_engine: "memory".into(),
				n_events: 0,
			};
			assert_eq!(status, StatusCode::OK);
			assert_eq!(actual, expected);
		}

		// Write events to db
		{
			let events = vec![1, 2, 3];

			let req = http::Request::builder()
				.method("PUT")
				.uri(format!("/db/{db_name}/e"))
				.header(http::header::CONTENT_TYPE, "application/json")
				.body(Body::from(serde_json::to_vec(&events).unwrap()))
				.unwrap();

			let res = result(&mut app, req).await;
			let status = res.status().clone();
			let actual: HashSet<String> = body(res).await;

			assert_eq!(status, StatusCode::CREATED);
			assert_eq!(actual.len(), events.len());
		}
	}
}
