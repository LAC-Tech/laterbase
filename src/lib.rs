
type NodeID = uuid::Uuid;
type Key = ulid::Ulid;
type Events = std::collections::BTreeMap<Key, Vec<u8>>;

#[derive(Debug)]
#[cfg_attr(test, derive(Clone))]
pub struct DB {
	id: NodeID,
	events: Events,
	changes: Vec<Key>,
	vector_clock: std::collections::HashMap<NodeID, usize>
}

impl DB {
	pub fn new() -> Self {
		let id = uuid::Uuid::new_v4();
		let events = std::collections::BTreeMap::new();
		let changes = vec![];
		let vector_clock = std::collections::HashMap::new();
		Self {id, events, changes, vector_clock}
	}

	pub fn add(&mut self, data: Vec<u8>) -> Key {
		let id = ulid::Ulid::new();
		self.events.insert(id, data);
		self.changes.push(id);
		id
	}

	fn lookup(&self, keys: Key) -> &[u8] {
		&self.events[&keys]
	}

	fn events_i_dont_have(
		&mut self, 
		node_id: NodeID, 
		remote_keys: &[Key]
	) -> (Vec<Key>, Events) {
		let start_index = self.start_index(node_id);
		let local_keys = &self.changes[start_index..];

		let mut result = std::collections::BTreeMap::new();
		let mut keys_i_need = vec![];

		if remote_keys.is_empty() {
			return (self.changes.clone(), self.events.clone())
		}

		for k in remote_keys {
			if !local_keys.contains(k) {
				keys_i_need.push(*k);
			} else {
				let event = self.events.get(k)
					.expect("Changes feed should match events recorded")
					.clone();

				result.insert(*k, event);
			}
		}

		(keys_i_need, result)
	}

	fn start_index(&self, node_id: NodeID) -> usize {
		*self.vector_clock.get(&node_id).unwrap_or(&0)
	}

	fn update_vector_clock(&mut self, other: &DB) {
		let new_counter = other.changes.len().checked_sub(1).unwrap_or(0);
		self.vector_clock.insert(other.id, new_counter);
	}

	pub fn merge(&mut self, other: &mut DB) {
		/*
			Self: "Here's all the ID's I've added since our last sync. Can you return the events I don't have?"
		*/
		let start_index = self.start_index(other.id);
		let added_ids = &self.changes[start_index..];

		println!("added ids:");
		println!("{:?}", added_ids);

		/*
			Other: "Certainly. And here's the ID's you sent that I don't have. Can you send them as well?"
		*/
		let (missing_keys, new_events) = 
			other.events_i_dont_have(self.id, added_ids);
	
		self.events.extend(new_events.clone().into_iter());
		self.update_vector_clock(other);

		for key in missing_keys {
			other.events.insert(key, self.events.get(&key).unwrap().clone());
		}

		other.update_vector_clock(self);
		/*
			Self: Ok no problem
		*/
	}
}

impl std::fmt::Display for DB {
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

impl PartialEq for DB {
	fn eq(&self, other: &Self) -> bool {
    	// Events are the source of truth!
        self.events == other.events 
    }
}

// Equality of event streams is reflexive
impl Eq for DB {}

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
	fn arb_db_pairs() -> impl Strategy<Value = (DB, DB)> {
		arb_byte_vectors().prop_map(|byte_vectors| {
			let mut db1 = DB::new();

			for byte_vec in byte_vectors {
		        db1.add(byte_vec);
		    }

		    let db2 = db1.clone();
		    (db1, db2)
		})
	}

	proptest! {
		#[test] 
		fn can_add_and_query_single_element(val in arb_bytes()) {
			let mut db = DB::new();
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
	}
}
