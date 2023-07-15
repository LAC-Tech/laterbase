
type NodeID = uuid::Uuid;
type Key = ulid::Ulid;
type Val = Vec<u8>

#[derive(Debug)]
#[cfg_attr(test, derive(Clone))]
struct VectorClock(std::collections::HashMap<NodeID, usize>);

impl VectorClock {
	fn new() -> Self {
		Self(std::collections::HashMap::new())
	}

	fn get(&mut self, node_id: NodeID) -> usize {
		*self.0.get(&node_id).unwrap_or(&0)
	}

	fn update(&mut self, other: &Node) {
		let new_counter = other.changes.len().checked_sub(1).unwrap_or(0);
		self.0.insert(other.id, new_counter);
	}
}

#[derive(Debug)]
#[cfg_attr(test, derive(Clone))]
struct Events {
	btree: std::collections::BTreeMap<Key, Val>,
	keys: Vec<Key>
}

impl Events {
	fn new() -> Self {
		Self { btree: std::collections::BTreeMap::new(), keys: vec![] }
	}

	fn add(&mut self, k: Key, v: Val) {
		self.btree.insert(k, v);
		self.keys.push(k)
	}

	fn lookup(&self, k: &Key) -> &Val {
		&self.btree[k]
	}

	fn added_since_last_sync(&self, logical_clock: usize) -> &[Key] {
		&self.keys[logical_clock..]
	}
}

#[derive(Debug)]
#[cfg_attr(test, derive(Clone))]
pub struct Node {
	id: NodeID,
	events: Events,
	vector_clock: VectorClock
}

impl Node {
	pub fn new() -> Self {
		let id = uuid::Uuid::new_v4();
		let events = Events::new();
		let vector_clock = VectorClock::new();
		Self {id, events, vector_clock}
	}

	pub fn add(&mut self, v: Val) -> Key {
		let k = ulid::Ulid::new();
		self.events.add(k, v);
		k
	}

	fn added_since_last_sync_with(&self, remote_id: NodeID) -> &[Key] {
		let logical_clock = self.vector_clock.get(remote_id);
		self.events.added_since_last_sync(logical_clock)
	}

	fn sync_handshake(&mut self, remote_id: NodeID, remote_keys: &[Key]) {
		let local_keys = self.added_since_last_sync_with(remote_id);	

		let missing_local_ids = vec![];
		let missing_remote_ids = vec![];

		for remote_key in remote_keys {
			if local_keys.contains(remote_key) {

			} else {

			}
		}

	}

	pub fn merge(&mut self, remote: &mut Node) {
		let keys_added_since_last_sync = {
			let logical_clock = self.vector_clock.get(remote.id);
			self.events.added_since_last_sync(logical_clock)
		};

		for k in keys_added_since_last_sync.iter() {
			if remote.changes.contains(k) {
				// Remote must have received them from another node
				continue
			}

			remote.events.insert(*k, self.events[k]);
		}
	}
}

impl std::fmt::Display for Node {
    fn fmt(&self, f: &mut std::fmt::Formatter) -> std::fmt::Result {
        writeln!(f, "EVENTS:")?;

        for (k, v) in self.events.iter() {
        	writeln!(f, "{} -> {:?}", k, v)?;
        }

        writeln!(f, "CHANGES:")?;
        for c in self.changes.iter() {
        	writeln!(f, "{}", c)?;
        }

        Ok(())
    }
}

impl PartialEq for Node {
	fn eq(&self, other: &Self) -> bool {
    	// Events are the source of truth!
        self.events == other.events 
    }
}

// Equality of event streams is reflexive
impl Eq for Node {}

#[cfg(test)]
mod tests {
    use super::*;
	use proptest::prelude::*;

	const N_BYTES_MAX: usize = 8;
	const N_VALS_MAX: usize = 2;

	fn arb_bytes() -> impl Strategy<Value = Vec<u8>> {
		prop::collection::vec(any::<u8>(), 0..=N_BYTES_MAX)
	}

	fn arb_byte_vectors() -> impl Strategy<Value = Vec<Vec<u8>>> {
		prop::collection::vec(arb_bytes(), 0..N_VALS_MAX)
	}

	/*
		Generate identical pairs of databases, that can be independently
		mutated to prove algebraic properties of CRDTs
	*/
	fn arb_db_pairs() -> impl Strategy<Value = (Node, Node)> {
		arb_byte_vectors().prop_map(|byte_vectors| {
			let mut db1 = Node::new();

			for byte_vec in byte_vectors {
		        db1.add(byte_vec);
		    }

		    let db2 = db1.clone();
		    (db1, db2)
		})
	}

	#[test]
	fn query_empty_vector_clock() {
		let mut vc = VectorClock::new();

		assert_eq!(vc.get(uuid::Uuid::new_v4()), 0);
	}

	proptest! {
		#[test] 
		fn can_add_and_query_single_element(val in arb_bytes()) {
			let mut db = Node::new();
			let id = db.add(val.clone());
			assert_eq!(db.lookup(id), &val);
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

		/*
		// a . b = b . a
		#[test]
		fn commutative(
			(mut db_left_a, mut db_right_a) in arb_db_pairs(), 
			(mut db_left_b, mut db_right_b) in arb_db_pairs(),
		) {
			// println!("--------");
			// println!("a . b");
			// println!("--------");
			// println!("{}", &db_left_a); 
			// println!("{}", &db_left_b);
			db_left_a.merge(&mut db_left_b);
			// println!("{}", &db_left_a);

			// println!("--------");
			// println!("b . a");
			// println!("--------");
			// println!("{}", &db_right_b); 
			// println!("{}", &db_right_a);
			db_right_b.merge(&mut db_right_a);
			// println!("{}", &db_right_b);

			assert_eq!(db_left_a, db_right_b);
		}
		*/
	}
}
