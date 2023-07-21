use std::collections::{BTreeMap, BTreeSet, HashMap};

pub trait Val: std::cmp::PartialEq + Clone + Send + std::marker::Sync {}
impl<T: PartialEq + Clone + Send + std::marker::Sync> Val for T {}

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

#[derive(
	Clone,
	Copy,
	Debug,
	PartialEq,
	PartialOrd,
	Eq,
	Ord,
	serde::Serialize,
	serde::Deserialize
)]
pub struct Key {
	#[serde(with = "ulid::serde::ulid_as_u128")]
	ulid: ulid::Ulid
}

impl Key {
	fn new() -> Self {
		Self { ulid: ulid::Ulid::new() }
	}
}

type Dbid = uuid::Uuid;

#[derive(Clone, Debug)]
pub struct Mem<V> {
	id: Dbid,
	events: BTreeMap<Key, V>,
	changes: Vec<Key>,
	vector_clock: VectorClock,
}

impl<V: Val> Mem<V> {
	pub fn new() -> Self {
		let id = uuid::Uuid::new_v4();
		let events = BTreeMap::new();
		let changes = vec![];
		let vector_clock = VectorClock::new();
		//let views = std::collections::HashMap::new();
		Self {id, events, changes, vector_clock}
	}

	pub fn add_local(&mut self, vs: &[V]) -> Vec<Key> {
		let mut keys = vec![];

		for v  in vs {
			let k = Key::new();
			self.events.insert(k, v.clone());
			self.changes.push(k);
			keys.push(k)
		}

		keys
	}

	pub fn get<'a>(&'a self, ks: &'a [Key]) -> impl Iterator<Item = &'a V> {
		ks.iter().filter_map(|k| self.events.get(k))
	}

	fn add_remote(
		&mut self, 
		remote_id: Dbid, 
		remote_events: BTreeMap<Key, V>
	) {
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
	) -> BTreeMap<Key, V> {
		local_ks.difference(remote_ks)
			.map(|remote_key| {
				let val = self.events
					.get(remote_key)
					.expect("database to be consistent");

				(*remote_key, val.clone())
			})
			.collect()
	}

	pub fn merge(&mut self, remote: &mut Mem<V>) {
		let local_ks = self.keys_added_since_last_sync(remote.id);
		let remote_ks = remote.keys_added_since_last_sync(self.id);

		let missing_events = remote.missing_events(&remote_ks, &local_ks);
		self.add_remote(remote.id, missing_events);

		let missing_events = self.missing_events(&local_ks, &remote_ks);	
		remote.add_remote(self.id, missing_events);
	}
}

impl<V: Val> PartialEq for Mem<V> {
	fn eq(&self, other: &Self) -> bool {
		// Events are the source of truth!
		self.events == other.events
	}
}

// Equality of event streams is reflexive
impl<V: Val> Eq for Mem<V> {}

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
	fn arb_db_pairs() -> impl Strategy<Value = (Mem<Vec<u8>>, Mem<Vec<u8>>)> {
		arb_byte_vectors().prop_map(|byte_vectors| {
			let mut db1 = Mem::new();

			db1.add_local(&byte_vectors);

			let db2 = db1.clone();

			(db1, db2)
		})
	}

	#[test]
	fn query_empty_vector_clock() {
		let vc = VectorClock::new();
		assert_eq!(vc.get(uuid::Uuid::new_v4()), 0);
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

