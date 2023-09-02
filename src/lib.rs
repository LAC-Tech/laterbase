//! Laterbase is a bi-temporal event store. This means it has two distinc concepts of time: transaction time and valid time.

use std::collections::BTreeMap;

#[cfg(target_endian = "big")]
compile_error!("I have assumed little endian throughout this codebase");

#[cfg(not(target_pointer_width = "64"))]
compile_error!("This code assumes a 64-bit target architecture");

mod time {
	/// When the database recored an event
	#[repr(transparent)]
	#[derive(
		rkyv::Archive, rkyv::Deserialize, rkyv::Serialize, Debug, PartialEq,
	)]
	pub struct Transaction<T>(T);

	/// When the event happened in the real world
	#[repr(transparent)]
	pub struct Valid<T>(T);
}

mod clock {
	pub type Logical = usize;
	pub type Physical = std::time::SystemTime;
}

mod event {
	use super::{clock, time};

	/**
	 * IDs must be globally unique and orderable. They should contain within 
	 * them the physical valid time. This is so clients can generate their own
	 * IDs.
	 * 
	 * TODO: make sure the physical time is not greater than current time.
	 */
	pub trait ID: std::cmp::Ord + std::marker::Copy {
		fn time(&self) -> time::Valid<clock::Physical>;
	}
	pub trait Val: std::marker::Copy {}
}

trait Address: std::marker::Copy {}

/** All of these must be idempotent */
#[derive(rkyv::Archive, rkyv::Deserialize, rkyv::Serialize, Debug)]
enum Message<'a, Addr: Address, ID: event::ID, E: event::Val> {
	// TODO: send them back where?
	SendBackTheseEvents((Addr, &'a [ID])),
	SendBackEventsSince((Addr, time::Transaction<clock::Logical>)),
	StoreEvents(BTreeMap<ID, E>),
}

/** 
 * The mysterious substances that exists between replicas..
 * HTTP network? Unix sockets? Who knows!
 */ 
trait Ether<Addr: Address, EventID: event::ID, Event: event::Val> {
	fn send(&mut self, addr: Addr, msgs: &[Message<Addr, EventID, Event>]);
}

/**
 * A replica is not only a database, but an implementation of an Actor.
 */
mod replica {
	use super::{event, Message, Ether, Address};
	use std::collections::BTreeMap;

	/// TODO: Assuming all replicas communicate over the same protocol
	#[derive(Debug)]
	struct Replica<Addr: Address, ID: event::ID, E: event::Val> {
		addr: Addr,
		events: BTreeMap<ID, E>,
		/// Events added in local append order
		append_log: Vec<ID>,
	}

	impl<Addr: Address, ID: event::ID, E: event::Val> Replica<Addr, ID, E> {
		fn new(addr: Addr) -> Self {
			let events = BTreeMap::new();
			let append_log = vec![];
			Self { addr, events, append_log }
		}

		fn send<Eth: Ether<Addr, ID, E>>(
			&mut self,
			eth: &mut Eth, 
			msgs: &[Message<Addr, ID, E>]
		) {
			for msg in msgs {
				match msg {
					Message::SendBackTheseEvents((addr, ids)) => {
						let events_to_send: BTreeMap<ID, E> = ids
							.into_iter()
							.filter_map(|id| {
								self.events.get(id).map(|e| (*id, *e))
							})
							.collect();

						let msgs = &[Message::StoreEvents(events_to_send)];

						eth.send(*addr, msgs);
					}
					Message::StoreEvents(events) => {
						for (id, val) in events {
							// TODO: super inefficient copying in a loop
							self.events.insert(*id, *val);
						}
					}
					_ => panic!("oh no!"),
				}
			}
		}
	}
}

//mod db;
//mod storage;

/*
#[derive(Clone)]
pub struct AppState<E: db::Event, S: storage::Storage> {
	dbs: Arc<RwLock<BTreeMap<String, db::DB<E, S>>>>,
}

impl<E: db::Event, S: db::StorageEngine> AppState<E, S> {
	fn new() -> Self {
		Self {
			dbs: Arc::new(RwLock::new(BTreeMap::new())),
		}
	}

	fn read_dbs(&self) -> RwLockReadGuard<BTreeMap<String, db::DB<E, S>>> {
		self.dbs.read().unwrap()
	}

	fn write_dbs(&self) -> RwLockWriteGuard<BTreeMap<String, db::DB<E, S>>> {
		self.dbs.write().unwrap()
	}
}

async fn create_db<E: db::Event>(
	Path(name): Path<String>,
	State(state): State<AppState<E, db::InMemoryStorageEngine>>,
) -> impl response::IntoResponse {
	let mut dbs = state.write_dbs();
	dbs.insert(name, db::simulated());
	http::StatusCode::CREATED
}

#[derive(serde::Deserialize)]
struct BulkRead {
	keys: Vec<db::Key>,
}

async fn db_info<E: db::Event, S: db::StorageEngine>(
	Path(db_name): Path<String>,
	State(state): State<AppState<E, S>>,
) -> Result<(http::StatusCode, axum::Json<db::Info>), http::StatusCode> {
	let dbs = state.read_dbs();
	let db = dbs.get(&db_name).ok_or(http::StatusCode::NOT_FOUND)?;
	Ok((http::StatusCode::OK, Json(db.info())))
}

async fn bulk_read<E: db::Event + Serialize, S: db::StorageEngine>(
	Query(db_name): Query<String>,
	Query(BulkRead { keys }): Query<BulkRead>,
	State(state): State<AppState<E, S>>,
) -> Result<axum::Json<Vec<E>>, http::StatusCode> {
	let dbs = state.read_dbs();
	let db = dbs.get(&db_name).ok_or(http::StatusCode::NOT_FOUND)?;
	let events = db.get(&keys).cloned();
	Ok(Json(events.collect()))
}

async fn bulk_write<
	E: db::Event + Serialize + de::DeserializeOwned,
	S: db::StorageEngine,
>(
	State(state): State<AppState<E, S>>,
	Path(db_name): Path<String>,
	Json(values): Json<Vec<E>>,
) -> Result<(http::StatusCode, axum::Json<Vec<String>>), http::StatusCode> {
	let mut dbs = state.write_dbs();
	let db = dbs.get_mut(&db_name).ok_or(http::StatusCode::NOT_FOUND)?;
	let new_keys = db
		.add_local(&values)
		.iter()
		.map(|k| k.to_string())
		.collect();
	Ok((http::StatusCode::CREATED, Json(new_keys)))
}

pub fn router<E, S>() -> Router
where
	E: db::Event + Serialize + 'static + de::DeserializeOwned,
	S: db::StorageEngine
{
	Router::new()
		.route("/db/:name", routing::post(create_db::<E>))
		.route("/db/:name", routing::get(db_info::<E, S>))
		.route("/db/:name/e", routing::put(bulk_write::<E, S>))
		.route("/db/:name/e/:args", routing::get(bulk_read::<E, S>))
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

	async fn result(
		app: &mut axum::Router,
		req: http::Request<Body>,
	) -> http::Response<BoxBody> {
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
*/
