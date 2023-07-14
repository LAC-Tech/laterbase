
type ID = ulid::Ulid;

#[derive(Debug, PartialEq)]
#[cfg_attr(test, derive(Clone))]
struct DB {
	events: std::collections::BTreeMap<ID, u8>,
	changes: Vec<ID>
}

impl DB {
	fn new() -> Self {
		let events = std::collections::BTreeMap::new();
		let changes = vec![];
		Self {events, changes}
	}

	fn add(&mut self, data: u8) -> ID {
		let id = ulid::Ulid::new();
		self.events.insert(id, data);
		id
	}

	fn lookup(&self, id: ID) -> u8 {
		self.events[&id]
	}

	fn merge(&mut self, other: &DB) {
		self.events.extend(other.events.clone().into_iter());
	}
}

#[cfg(test)]
mod tests {
    use super::*;
	use proptest::prelude::*;

	fn arb_bytes(max_len: usize) -> impl Strategy<Value = Vec<u8>> {
		prop::collection::vec(any::<u8>(), 0..=max_len)
	}

	/*
		Generate identical pairs of databases, that can be independently
		mutated to prove algebraic properties of CRDTs
	*/
	fn arb_db_pairs(max_n_bytes: usize) -> impl Strategy<Value = (DB, DB)> {
		arb_bytes(max_n_bytes).prop_map(|bytes| {
	        let mut db1 = DB::new();

	        for b in &bytes {
	            db1.add(*b);
	        }

	        let db2 = db1.clone();

	        (db1, db2)
	    })
	}

	const N_BYTES: usize = 5;
	proptest! {
		#[test] 
		fn can_add_and_query_single_element(b in u8::MIN..u8::MAX) {
			let mut db = DB::new();
			let id = db.add(b);
			assert_eq!(db.lookup(id), b);
		}

		#[test]
		fn idempotent((mut db1, db2) in arb_db_pairs(N_BYTES)) {
			db1.merge(&db2);
			assert_eq!(db1, db2);
		}
	
		// (a . b) . c = a . (b . c)
		#[test]
		fn associative(
			(mut db_left_a, mut db_right_a) in arb_db_pairs(N_BYTES), 
			(db_left_b, mut db_right_b) in arb_db_pairs(N_BYTES),
			(db_left_c, db_right_c) in arb_db_pairs(N_BYTES)
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
			(mut db_left_a, db_right_a) in arb_db_pairs(N_BYTES), 
			(db_left_b, mut db_right_b) in arb_db_pairs(N_BYTES),
		) {

			db_left_a.merge(&db_left_b);
			db_right_b.merge(&db_right_a);
			assert_eq!(db_left_a, db_right_b);
		}
	}
}
