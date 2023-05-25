use std::collections::HashSet;
use std::hash::Hash;

use rand::prelude::*;

#[derive(Debug, PartialEq)]
struct DB {
	events: std::collections::HashSet<u8>
}

impl DB {
	fn new() -> Self {
		Self { events: std::collections::HashSet::new()}
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

	proptest! {
		#[test] 
		fn can_add_and_query_single_element(n in u8::MIN..u8::MAX) {
			let mut db = DB::new();

			db.add(n);

			assert!(db.lookup(n));
		}

		#[test]
		fn idempotent(len in 0..0xFF) {
			let mut rng = rand::rngs::StdRng::from_entropy();
			let mut bytes = vec![0u8; len as usize];
			rng.fill_bytes(&mut bytes);

			let mut db1 = DB::new();
			let mut db2 = DB::new();

			for b in bytes {
				db1.add(b);
				db2.add(b);
			}

			db1.merge(&db2);
			

			assert_eq!(db1, db2);

		}
	}

}
