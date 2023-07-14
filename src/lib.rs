
type ID = ulid::Ulid;

#[derive(Debug)]
#[cfg_attr(test, derive(Clone))]
struct DB {
	events: std::collections::BTreeMap<ID, Vec<u8>>,
	changes: Vec<ID>
}

impl DB {
	fn new() -> Self {
		let events = std::collections::BTreeMap::new();
		let changes = vec![];
		Self {events, changes}
	}

	fn add(&mut self, data: Vec<u8>) -> ID {
		let id = ulid::Ulid::new();
		self.events.insert(id, data);
		self.changes.push(id);
		id
	}

	fn lookup(&self, id: ID) -> &[u8] {
		&self.events[&id]
	}

	fn merge(&mut self, other: &DB) {
		self.events.extend(other.events.clone().into_iter());
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
	const N_VALS_MAX: usize = 256;

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
		fn idempotent((mut db1, db2) in arb_db_pairs()) {
			db1.merge(&db2);
			assert_eq!(db1, db2);
		}
	
		// (a . b) . c = a . (b . c)
		#[test]
		fn associative(
			(mut db_left_a, mut db_right_a) in arb_db_pairs(), 
			(db_left_b, mut db_right_b) in arb_db_pairs(),
			(db_left_c, db_right_c) in arb_db_pairs()
		) {
			db_left_a.merge(&db_left_b);
			db_left_a.merge(&db_left_c);
			
			db_right_b.merge(&db_right_c);
			db_right_a.merge(&db_right_b);

			assert_eq!(db_left_a, db_right_a);
		}

		// a . b = b . a
		#[test]
		fn commutative(
			(mut db_left_a, db_right_a) in arb_db_pairs(), 
			(db_left_b, mut db_right_b) in arb_db_pairs(),
		) {
			db_left_a.merge(&db_left_b);
			db_right_b.merge(&db_right_a);
			assert_eq!(db_left_a, db_right_b);
		}
	}
}
