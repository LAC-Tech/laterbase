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

	fn arb_db() -> impl Strategy<Value = DB> {
        prop::collection::btree_set(0u8..=255u8, 0..100)
            .prop_map(|events| DB { events })
	}

	fn arb_bytes(max_len: usize) -> impl Strategy<Value = Vec<u8>> {
		prop::collection::vec(any::<u8>(), 0..=max_len)
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
		fn idempotent(bytes in arb_bytes(N_BYTES)) {
			let mut db1 = DB::new();
			let mut db2 = DB::new();

			for b in bytes {
				db1.add(b);
				db2.add(b);
			}

			db1.merge(&db2);
			assert_eq!(db1, db2);
		}
		
		// (a . b) . c = a . (b . c)
		#[test]
		fn associative(
			a_bytes in arb_bytes(N_BYTES), 
			b_bytes in arb_bytes(N_BYTES),
			c_bytes in arb_bytes(N_BYTES)
		) {
			let (mut db_left_a, mut db_left_b, mut db_left_c) = 
				(DB::new(), DB::new(), DB::new());

			let (mut db_right_a, mut db_right_b, mut db_right_c) = 
				(DB::new(), DB::new(), DB::new());

			for b in a_bytes {
				db_left_a.add(b);
				db_right_a.add(b);
			}

			for b in b_bytes {
				db_left_b.add(b);
				db_right_b.add(b);
			}

			for b in c_bytes {
				db_left_c.add(b);
				db_right_c.add(b);
			}

			db_left_a.merge(&db_left_b);
			db_left_a.merge(&db_left_c);

			db_right_b.merge(&db_right_c);
			db_right_a.merge(&db_right_b);

			assert_eq!(db_left_a, db_right_a);
		}
	}
}
