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

	proptest! {
		#[test] 
		fn can_add_and_query_single_element(n in u8::MIN..u8::MAX) {
			let mut db = DB::new();
			db.add(n);
			assert!(db.lookup(n));
		}

		#[test]
		fn idempotent(bytes in arb_bytes(500)) {
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
		fn associative(len1 in 0..0xFF, len2 in 0..0xFF, len3 in 0..0xFF) {
			let mut rng = rand::random();
			let mut bytes1 = vec![0u8; len1 as usize];
			let mut bytes2 = vec![0u8; len2 as usize];
			let mut bytes3 = vec![0u8; len2 as usize];

			let mut db1 = DB::new();
			let mut db2 = DB::new();
			let mut db3 = DB::new();

			for b in bytes1 {
				db1.add(b);
			}

			for b in bytes2 {
				db2.add(b);
			}

			for b in bytes3 {
				db3.add(b);
			}
		}
	}
}
