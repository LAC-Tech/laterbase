#[derive(Debug, PartialEq)]
struct DB {
	events: std::collections::BTreeSet<u8>
}

impl DB {
	fn new() -> Self {
		Self { events: std::collections::BTreeSet::new() }
	}

	fn add(&mut self, data: u8) {
		self.events.insert(data);
	}

	fn lookup(&self, data: u8) -> bool {
		self.events.contains(&data)
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

	fn arb_db_pairs(max_n_bytes: usize) -> impl Strategy<Value = (DB, DB)> {
		arb_bytes(max_n_bytes).prop_map(|bytes| {
	        let mut db1 = DB::new();
	        let mut db2 = DB::new();

	        for b in &bytes {
	            db1.add(*b);
	            db2.add(*b);
	        }

	        (db1, db2)
	    })
	}

	const N_BYTES: usize = 500;
	proptest! {
		#[test] 
		fn can_add_and_query_single_element(n in u8::MIN..u8::MAX) {
			let mut db = DB::new();
			db.add(n);
			assert!(db.lookup(n));
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
	}
}
