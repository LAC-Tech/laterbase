#![forbid(unsafe_code)] // Try and be a good boy
use std::collections::{BTreeMap, BTreeSet, HashMap};

pub trait Event:
	std::cmp::PartialEq + Clone + Send + std::marker::Sync + bytemuck::Pod
{
}
impl<T: PartialEq + Clone + Send + std::marker::Sync + bytemuck::Pod> Event
	for T
{
}

#[derive(Debug, PartialEq, serde::Deserialize, serde::Serialize)]
pub struct Info {
	pub storage_engine: String,
	pub n_events: usize,
}

trait StorageEngine {
	fn info() -> Info;

	// TODO: failure modes for writing
	fn write_event(&mut self, k: Key, event: &[u8]);
	fn write_change(&mut self, k: Key);
}

/*
 * TODO: why did I wrap ulid around this? there was a reason
 * maybe it's related to all of these things I'm deriving...
 */
#[derive(
	Clone,
	Copy,
	Debug,
	PartialEq,
	PartialOrd,
	Eq,
	Ord,
	serde::Serialize,
	serde::Deserialize,
)]
pub struct Key {
	#[serde(with = "ulid::serde::ulid_as_u128")]
	ulid: ulid::Ulid,
}

impl Key {
	fn new() -> Self {
		Self {
			ulid: ulid::Ulid::new(),
		}
	}
}

impl std::fmt::Display for Key {
	fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
		write!(f, "{}", self.ulid)
	}
}

type Dbid = uuid::Uuid;

#[derive(Clone, Debug)]
pub struct Mem<E> {
	id: Dbid,
	events: BTreeMap<Key, Box<[u8]>>,
	changes: Vec<Key>,
	vector_clock: HashMap<Dbid, usize>,
	_e: std::marker::PhantomData<E>,
}

impl<E: Event> Mem<E> {
	pub fn new() -> Self {
		let id = uuid::Uuid::new_v4();
		let events = BTreeMap::new();
		let changes = vec![];
		let vector_clock = HashMap::new();
		Self {
			id,
			events,
			changes,
			vector_clock,
			_e: std::marker::PhantomData,
		}
	}

	pub fn info(&self) -> Info {
		Info {
			storage_engine: "memory".into(),
			n_events: self.events.len(),
		}
	}

	pub fn add_local(&mut self, es: &[E]) -> Vec<Key> {
		let mut keys = vec![];

		for e in es {
			let k = Key::new();
			self.events.insert(k, e.clone());
			self.changes.push(k);
			keys.push(k)
		}

		keys
	}

	pub fn get<'a>(&'a self, ks: &'a [Key]) -> impl Iterator<Item = &'a E> {
		let events = ks.iter().filter_map(|k| self.events.get(k));
		events.map(|bytes| bytemuck::from_bytes(&*bytes))
	}

	pub fn add_remote(
		&mut self,
		remote_id: Dbid,
		remote_events: BTreeMap<Key, E>,
	) {
		self.changes.extend(remote_events.keys());
		self.events.extend(remote_events);
		let lamport_clock = self.changes.len().saturating_sub(1);
		self.vector_clock.insert(remote_id, lamport_clock);
	}

	pub fn keys_added_since_last_sync(&self, remote_id: Dbid) -> BTreeSet<Key> {
		let lamport_clock = self.vector_clock.get(&remote_id);
		// default to 0 if we don't yet have an entry
		let lamport_clock = *lamport_clock.unwrap_or(&0);
		self.changes[lamport_clock..].iter().cloned().collect()
	}

	pub fn missing_events(
		&self,
		local_ks: &BTreeSet<Key>,
		remote_ks: &BTreeSet<Key>,
	) -> BTreeMap<Key, E> {
		local_ks
			.difference(remote_ks)
			.map(|k| {
				let bytes =
					self.events.get(k).expect("database to be consistent");
				let e = bytemuck::from_bytes(&*bytes);
				(*k, e.clone())
			})
			.collect()
	}

	pub fn init_sync(
		&self,
		remote_id: Dbid,
		remote_keys_new: &BTreeSet<Key>,
	) -> SyncResponse<E> {
		let local_keys_new = self.keys_added_since_last_sync(remote_id);

		let missing_keys: BTreeSet<Key> = local_keys_new
			.difference(remote_keys_new)
			.cloned()
			.collect();

		let new_events: BTreeMap<Key, E> =
			self.missing_events(&local_keys_new, remote_keys_new);

		SyncResponse {
			missing_keys,
			new_events,
		}
	}
}

// TODO: stupid name
pub struct SyncResponse<E> {
	pub missing_keys: BTreeSet<Key>,
	pub new_events: BTreeMap<Key, E>,
}

pub fn merge<E: Event>(local: &mut Mem<E>, remote: &mut Mem<E>) {
	let local_keys_new = local.keys_added_since_last_sync(remote.id);
	let remote_sync_res = remote.init_sync(local.id, &local_keys_new);

	local.add_remote(remote.id, remote_sync_res.new_events);
	let remote_events_new =
		local.missing_events(&local_keys_new, &remote_sync_res.missing_keys);

	remote.add_remote(local.id, remote_events_new);
}

impl<V: Event> PartialEq for Mem<V> {
	fn eq(&self, other: &Self) -> bool {
		// Events are the source of truth!
		self.events == other.events
	}
}

// Equality of event streams is reflexive
impl<V: Event> Eq for Mem<V> {}

#[cfg(test)]
mod tests {
	use pretty_assertions::assert_eq;

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
	fn arb_db_pairs() -> impl Strategy<Value = (Mem<Vec<u8>>, Mem<Vec<u8>>)> {
		arb_byte_vectors().prop_map(|events| {
			let mut db1 = Mem::new();

			db1.add_local(&events);

			let db2 = db1.clone();

			(db1, db2)
		})
	}

	proptest! {
		#[test]
		fn can_add_and_query_single_element(val in arb_bytes()) {
			let mut db = Mem::new();
			let keys = db.add_local(&val);
			let actual: Vec<u8> = db.get(&keys).cloned().collect();
			assert_eq!(actual, val)
		}

		#[test]
		fn idempotent((mut db1, mut db2) in arb_db_pairs()) {
			merge(&mut db1, &mut db2);
			assert_eq!(db1, db2);
		}

		// (a . b) . c = a . (b . c)
		#[test]
		fn associative(
			(mut db_left_a, mut db_right_a) in arb_db_pairs(),
			(mut db_left_b, mut db_right_b) in arb_db_pairs(),
			(mut db_left_c, mut db_right_c) in arb_db_pairs()
		) {
			merge(&mut db_left_a, &mut db_left_b);
			merge(&mut db_left_a, &mut db_left_c);

			merge(&mut db_right_b, &mut db_right_c);
			merge(&mut db_right_a, &mut db_right_b);

			assert_eq!(db_left_a, db_right_a);
		}

		// a . b = b . a
		#[test]
		fn commutative(
			(mut db_left_a, mut db_right_a) in arb_db_pairs(),
			(mut db_left_b, mut db_right_b) in arb_db_pairs(),
		) {
			merge(&mut db_left_a, &mut db_left_b);
			merge(&mut db_right_b, &mut db_right_a);

			assert_eq!(db_left_a, db_right_b);
		}
	}
}
