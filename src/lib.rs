use std::collections::{BTreeMap, BTreeSet, HashMap};
use axum::{body, extract, http, response, Router, routing};

type Dbid = uuid::Uuid;
type Event = (Key, Vec<u8>);
type Events = BTreeMap<Key, Vec<u8>>;

#[derive(Clone, Copy, Debug, PartialEq, PartialOrd, Eq, Ord, serde::Serialize, serde::Deserialize)]
pub struct Key {
    #[serde(with = "ulid::serde::ulid_as_u128")]
    ulid: ulid::Ulid
}

impl Key {
	fn new() -> Self {
		Self { ulid: ulid::Ulid::new() }
	}
}

#[derive(Clone, Debug)]
struct VectorClock(HashMap<Dbid, usize>);

impl VectorClock {
	fn new() -> Self {
		Self(HashMap::new())
	}

	fn get(&self, db_id: Dbid) -> usize {
		*self.0.get(&db_id).unwrap_or(&0)
	}

	fn update(&mut self, remote_id: Dbid, local_clock: usize) {
		self.0.insert(remote_id, local_clock);
	}
}

type ViewData = BTreeMap<Vec<u8>, Vec<u8>>; 
type ViewFn = fn(&ViewData, &[u8]) -> ViewData;

#[derive(Clone)]
pub struct View {
	data: ViewData,
	f: ViewFn
}

impl View {
	fn new(f: ViewFn) -> Self {
		let data = ViewData::new();
		Self { data, f }
	}

	fn get(&self, id: &[u8]) -> Option<&[u8]> {
		self.data.get(id).map(|bs| bs.as_slice())
	}
}

impl View {
	fn process(&mut self, event: &[u8]) {
		let new_data = (self.f)(&self.data, event);
		self.data.extend(new_data.into_iter());
	}
}

impl std::fmt::Debug for View {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("View")
            .field("btree", &self.data)
            .finish()
    }
}

#[cfg(test)]
mod test {
	use super::*;

	#[derive(Debug, serde::Serialize, serde::Deserialize)]
	struct TempReading {
		location: String,
		celcius: f32
	}

	#[derive(Debug, PartialEq, serde::Serialize, serde::Deserialize)]
	struct MeanAccum {
		count: usize,
		mean: f32
	}

	impl MeanAccum {
		fn add(&self, elem: f32) -> Self {
			let count = self.count + 1;
			let mean = (self.mean + elem) / (count as f32);

			Self { count, mean }
		}

		fn new(first_elem: f32) -> Self {
			Self { count: 1, mean: first_elem }
		}
	}

	#[test]
	fn can_add_numbers() {
		let events = [
			("a", 20.0),
			("b", 12.0),
			("c", 34.0),
			("a", 13.0),
			("b", -34.0)
		].map(|(location, celcius)| TempReading { 
			location: location.to_string(),
			celcius
		});
	
		let mut running_average = View::new(|accum, event| {
			let mut result = ViewData::new();
			let event: TempReading = bincode::deserialize(event).unwrap();
		
			let id = bincode::serialize(&event.location).unwrap();
			let mean_accum: MeanAccum = accum.get(&id).map(|existing_average| {
				let mean_accum: MeanAccum = 
					bincode::deserialize(existing_average.as_slice()).unwrap();
			
				mean_accum.add(event.celcius)

			})
			.unwrap_or(MeanAccum::new(event.celcius));

			let val = bincode::serialize(&mean_accum).unwrap();

			result.insert(id, val);
			result
		});

		for e in events {
			let e = bincode::serialize(&e).unwrap();
			running_average.process(e.as_slice());
		}

		let id: Vec<u8> = bincode::serialize("a").unwrap();
		let expected: Vec<u8> = bincode::serialize(
			&MeanAccum{count: 2, mean: 16.5 as f32}).unwrap();

        let expected: Option<MeanAccum> = 
            Some(bincode::deserialize(&expected).unwrap());

		let actual: Option<MeanAccum> =
			running_average.get(&id).map(|bs| bincode::deserialize(&bs).unwrap());

		assert_eq!(actual, expected);
	}
}

#[derive(Clone, Debug)]
pub struct DB {
	id: Dbid,
	events: Events,
	changes: Vec<Key>,
	vector_clock: VectorClock,
	//views: std::collections::HashMap<String, BTreeMap<String, View>>
}

impl DB {
	pub fn new() -> Self {
		let id = uuid::Uuid::new_v4();
		let events = Events::new();
		let changes = vec![];
		let vector_clock = VectorClock::new();
		//let views = std::collections::HashMap::new();
		Self {id, events, changes, vector_clock}
	}

	pub fn add_local(&mut self, v: &[u8]) -> Key {
		let k = Key::new();
		self.events.insert(k, v.to_vec());
		self.changes.push(k);
		k
	}

	pub fn get(&self, ks: &[Key]) -> Vec<&[u8]> {
		ks.iter()
			.filter_map(|k| self.events.get(k).map(|v| v.as_slice()))
			.collect()
	}

	fn add_remote(&mut self, remote_id: Dbid, remote_events: Events) {
		self.changes.extend(remote_events.keys());
		self.events.extend(remote_events);
		let logical_clock = self.changes.len().saturating_sub(1);
		self.vector_clock.update(remote_id, logical_clock);
	}

	fn keys_added_since_last_sync(&self, remote_id: Dbid) -> BTreeSet<Key> {
		let logical_clock = self.vector_clock.get(remote_id);
		self.changes[logical_clock..].iter().cloned().collect()
	}

	fn missing_events(
		&self,
		local_ks: &BTreeSet<Key>,
		remote_ks: &BTreeSet<Key>
	) -> Events {
		local_ks.difference(remote_ks).map(|remote_key| {
			let val = self.events
				.get(remote_key)
				.expect("database to be consistent");

			(*remote_key, val.clone())
		})
		.collect()
	}

	pub fn merge(&mut self, remote: &mut DB) {
		let local_ks = self.keys_added_since_last_sync(remote.id);
		let remote_ks = remote.keys_added_since_last_sync(self.id);

		let missing_events = remote.missing_events(&remote_ks, &local_ks);
		self.add_remote(remote.id, missing_events);

		let missing_events = self.missing_events(&local_ks, &remote_ks);	
		remote.add_remote(self.id, missing_events);
	}
}

impl PartialEq for DB {
	fn eq(&self, other: &Self) -> bool {
    	// Events are the source of truth!
        self.events == other.events
    }
}

// Equality of event streams is reflexive
impl Eq for DB {}

#[derive(Clone)]
struct AppState {
	dbs: BTreeMap<String, DB>
}

impl AppState {
	fn new() -> Self {
		Self { dbs: BTreeMap::new() }
	}
}

async fn create_db(
	extract::Path(name): extract::Path<String>,
	extract::State(mut state): extract::State<AppState>,
) {
	state.dbs.insert(name, DB::new());
}

#[derive(serde::Deserialize)]
struct BulkRead {
	keys: Vec<Key>
}

async fn bulk_read(
	extract::Query(db_name): extract::Query<String>,
	extract::Query(BulkRead {keys}): extract::Query<BulkRead>,
	extract::State(state): extract::State<AppState>
) -> impl response::IntoResponse {
	state.dbs.get(&db_name).map(|db| {
		db.get(keys)
	})
}

fn app() -> Router {
	Router::new()
		 .route("/db/:name", routing::post(create_db))
		 .with_state(AppState::new())
}

#[cfg(test)]
mod tests {
	use pretty_assertions::{assert_eq};

    use super::*;
	use proptest::prelude::*;

	const N_BYTES_MAX: usize = 2;
	const N_VALS_MAX: usize = 8;

	fn arb_bytes() -> impl Strategy<Value = Vec<u8>> {
        prop::collection::vec(any::<u8>(), 0..=N_BYTES_MAX)
	}

	fn arb_byte_vectors() -> impl Strategy<Value = Vec<Vec<u8>>> {
		prop::collection::vec(arb_bytes(), 0..=N_VALS_MAX)
	}

	/*
		Generate identical pairs of databases, that can be independently
		mutated to prove algebraic properties of CRDTs
	*/
	fn arb_db_pairs() -> impl Strategy<Value = (DB, DB)> {
		arb_byte_vectors().prop_map(|byte_vectors| {
			let mut db1 = DB::new();

			for byte_vec in byte_vectors {
				db1.add_local(&byte_vec);
			}

			let db2 = db1.clone();

		    (db1, db2)
		})
	}

	#[test]
	fn query_empty_vector_clock() {
		let vc = VectorClock::new();
		assert_eq!(vc.get(uuid::Uuid::new_v4()), 0);
	}

	use tower::Service; // for `call`
	use tower::ServiceExt; // for `oneshot` and `ready`
	
	#[tokio::test]
	async fn can_create_db() {
		let mut app = app();

        let req = http::Request::builder()
			.method("POST")
			.uri("/db/:name")
			.body(body::Body::empty())
			.unwrap();
        let res = app.ready().await.unwrap().call(req).await.unwrap();
        assert_eq!(res.status(), http::StatusCode::OK);

        let req = http::Request::builder()
			.method("GET")
			.uri("/db/:name")
			.body(body::Body::empty())
			.unwrap();
        let res = app.ready().await.unwrap().call(req).await.unwrap();
        assert_eq!(res.status(), http::StatusCode::OK);
	}

	proptest! {
		#[test]
		fn can_add_and_query_single_element(val in arb_bytes()) {
			let mut db = DB::new();
			let key = db.add_local(&val);
			let actual = db.get(&[key]).first().cloned();
			let expected: Option<&[u8]> = Some(&val);
			assert_eq!(actual, expected)
		}

		#[test]
		fn idempotent((mut db1, mut db2) in arb_db_pairs()) {
			db1.merge(&mut db2);
			assert_eq!(db1, db2);
		}

		// (a . b) . c = a . (b . c)
		#[test]
		fn associative(
			(mut db_left_a, mut db_right_a) in arb_db_pairs(), 
			(mut db_left_b, mut db_right_b) in arb_db_pairs(),
			(mut db_left_c, mut db_right_c) in arb_db_pairs()
		) {
			db_left_a.merge(&mut db_left_b);
			db_left_a.merge(&mut db_left_c);

			db_right_b.merge(&mut db_right_c);
			db_right_a.merge(&mut db_right_b);

			assert_eq!(db_left_a, db_right_a);
		}
		
		// a . b = b . a
		#[test]
		fn commutative(
			(mut db_left_a, mut db_right_a) in arb_db_pairs(), 
			(mut db_left_b, mut db_right_b) in arb_db_pairs(),
		) {
			db_left_a.merge(&mut db_left_b);
			db_right_b.merge(&mut db_right_a);

			assert_eq!(db_left_a, db_right_b);
		}
	}
}
