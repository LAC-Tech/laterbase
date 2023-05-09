use axum::{body, extract, http, response, routing, Router};
use rocksdb;

use std::sync::Arc;

struct AppState {
	db: rocksdb::DB
}

#[tokio::main]
async fn main() {
	let mut opts = rocksdb::Options::default();

	opts.set_log_level(rocksdb::LogLevel::Fatal);
	opts.create_if_missing(true);
	let db = rocksdb::DB::open(&opts, "../rocksdb_instance").unwrap();
	let shared_state = Arc::new(AppState { db});

	// build our application with a single route
	let app = Router::new()
		.route("/", routing::get(home))
		.route("/:key", routing::get(read).put(write))
		.with_state(shared_state);

	// run it with hyper on localhost:3000
	axum::Server::bind(&"0.0.0.0:3000".parse().unwrap())
		.serve(app.into_make_service())
		.await
		.unwrap();
}

async fn home(
	extract::State(state): extract::State<Arc<AppState>>
)  -> impl response::IntoResponse {
	use http::StatusCode;

	let properties = state.db.property_value(rocksdb::properties::AGGREGATED_TABLE_PROPERTIES);

	match properties {
		Ok(Some(s)) => (StatusCode::OK, s),
		_ => (StatusCode::INTERNAL_SERVER_ERROR, String::new())
	}
}

async fn read(
	extract::Path(key): extract::Path<String>,
	extract::State(state): extract::State<Arc<AppState>>
) -> impl response::IntoResponse {
	use http::StatusCode;

	match state.db.get(key) {
		Err(err) => (StatusCode::INTERNAL_SERVER_ERROR, err.to_string()),
		Ok(None) => (StatusCode::NOT_FOUND, "Key not found".to_string()),
		Ok(Some(val)) => match String::from_utf8(val) {
			Err(err) => (StatusCode::INTERNAL_SERVER_ERROR, err.to_string()),
			Ok(s) => (StatusCode::OK, s)
		}
	}
}


async fn write(
	extract::State(state): extract::State<Arc<AppState>>,
	req: body::Bytes
) -> axum::http::StatusCode {
    let body_bytes = req.to_vec();

    match state.db.put(b"lol", body_bytes) {
    	Ok(_) => http::StatusCode::CREATED,
    	Err(_) => http::StatusCode::INTERNAL_SERVER_ERROR
    }
}
