/*
use std::collections::{BTreeMap, BTreeSet};

pub trait Event:
	std::cmp::PartialEq + Clone + Send + std::marker::Sync + bytemuck::Pod
{
}
impl<T: PartialEq + Clone + Send + std::marker::Sync + bytemuck::Pod> Event
	for T
{
}

use crate::storage;

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
	bytemuck::Zeroable,
	bytemuck::Pod,
)]
#[repr(transparent)]
pub struct Key(u128);

impl Key {
	fn new() -> Self {
		Self(ulid::Ulid::new().0)
	}
}

impl std::fmt::Display for Key {
	fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
		write!(f, "{}", ulid::Ulid(self.0))
	}
}

type Dbid = uuid::Uuid;

#[derive(Debug, PartialEq, serde::Deserialize, serde::Serialize)]
pub struct Info {
	pub storage_engine: String,
	pub n_events: usize,
}

pub fn simulated<E: Event>() -> DB<E, storage::Simulated> {
	DB::new(storage::Simulated::new())
}

#[derive(Clone, Debug)]
pub struct DB<E, S: storage::Storage> {
	id: Dbid,
	storage: S,
	_e: std::marker::PhantomData<E>,
}

impl<E: Event, S: storage::Storage> DB<E, S> {
	fn new(storage_engine: S) -> Self {
		Self {
			id: uuid::Uuid::new_v4(),
			storage: storage_engine,
			_e: std::marker::PhantomData,
		}
	}

	pub fn info(&self) -> Info {
		Info {
			// This needs to be heap-allocated for.. reasons..
			storage_engine: String::from(S::NAME),
			n_events: self.storage.n_events(),
		}
	}

	pub fn add_local<'a>(
		&mut self,
		es: impl Iterator<Item = &'a E>,
	) -> Vec<Key> {
		let mut keys = vec![];

		for e in es {
			let key = Key::new();
			let key_bytes = bytemuck::bytes_of(&key);
			let event_bytes = bytemuck::bytes_of(e);
			self.storage.write_event(key_bytes, event_bytes);
			self.storage.write_change(key_bytes);
			keys.push(key)
		}

		keys
	}

	pub fn get<'a>(&'a self, ks: &'a [Key]) -> impl Iterator<Item = &'a E> {
		let events = ks
			.iter()
			.map(|k| bytemuck::bytes_of(k))
			.filter_map(|k| self.storage.read_event(k));
		events.map(|bytes| bytemuck::from_bytes(&*bytes))
	}

	pub fn add_remote(
		&mut self,
		remote_id: Dbid,
		remote_events: BTreeMap<Key, Box<[u8]>>,
	) {
		for new_key in remote_events.keys() {
			self.storage.write_change(bytemuck::bytes_of(new_key));
		}

		for (key, event) in remote_events.iter() {
			self.storage.write_event(bytemuck::bytes_of(key), event);
		}

		// saturing sub incase there are 0 events
		let logical_time = self.storage.n_events().saturating_sub(1);
		self.storage
			.update_vector_clock(&remote_id.into_bytes(), logical_time);
	}

	pub fn keys_added_since_last_sync(
		&self,
		remote_id: Dbid,
	) -> impl Iterator<Item = &Key> {
		let last_synced_with_remote = self
			.storage
			.read_vector_clock(&remote_id.into_bytes())
			.unwrap_or(0); // default to 0 if no entry for the DB exists

		self.storage
			.keys_added_since(last_synced_with_remote)
			.map(|bytes| bytemuck::from_bytes(bytes))
	}

	pub fn missing_events(
		&self,
		local_ks: &BTreeSet<Key>,
		remote_ks: &BTreeSet<Key>,
	) -> BTreeMap<Key, Box<[u8]>> {
		local_ks
			.difference(remote_ks)
			.map(|k| {
				let bytes = self
					.storage
					.read_event(bytemuck::bytes_of(k))
					.expect("db to be consistent");

				(*k, Box::from(bytes))
			})
			.collect()
	}

	pub fn init_sync(
		&self,
		remote_id: Dbid,
		remote_keys_new: &BTreeSet<Key>,
	) -> SyncResponse<Box<[u8]>> {
		let local_keys_new: BTreeSet<Key> = self
			.keys_added_since_last_sync(remote_id)
			.cloned()
			.collect();

		let missing_keys: BTreeSet<Key> = local_keys_new
			.difference(remote_keys_new)
			.cloned()
			.collect();

		let new_events = self.missing_events(&local_keys_new, remote_keys_new);

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

pub fn merge<E: Event, S: storage::Storage>(
	local: &mut DB<E, S>,
	remote: &mut DB<E, S>,
) {
	let local_keys_new: BTreeSet<Key> = local
		.keys_added_since_last_sync(remote.id)
		.cloned()
		.collect();
	let remote_sync_res = remote.init_sync(local.id, &local_keys_new);

	local.add_remote(remote.id, remote_sync_res.new_events);
	let remote_events_new =
		local.missing_events(&local_keys_new, &remote_sync_res.missing_keys);

	remote.add_remote(local.id, remote_events_new);
}

impl<E: Event, S: storage::Storage> PartialEq for DB<E, S> {
	fn eq(&self, other: &Self) -> bool {
		self.storage == other.storage
	}
}

// Equality of event streams is reflexive
impl<E: Event, S: storage::Storage> Eq for DB<E, S> {}

#[cfg(test)]
mod tests {
	use pretty_assertions::assert_eq;

	use super::*;
	use proptest::prelude::*;

	const N_BYTES_MAX: usize = 64;
	const N_VALS_MAX: usize = 256;

	fn arb_bytes() -> impl Strategy<Value = Vec<u8>> {
		prop::collection::vec(any::<u8>(), 0..=N_BYTES_MAX)
	}

	fn arb_byte_vectors() -> impl Strategy<Value = Vec<Vec<u8>>> {
		prop::collection::vec(arb_bytes(), 0..=N_VALS_MAX)
	}

	fn arb_storage_pairs(
	) -> impl Strategy<Value = (impl storage::Storage, impl storage::Storage)> {
		arb_byte_vectors().prop_map(|events| {
			let mut db1 = simulated();

			db1.add_local(events.iter().map(|e| e.as_slice()));

			let db2 = db1.clone();

			(db1, db2)
		})
	}

	/*
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
	*/
}
*/
