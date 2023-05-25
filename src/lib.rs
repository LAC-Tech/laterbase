use std::collections::HashSet;
use std::hash::Hash;

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

	fn merge(&mut self, other: DB) {
		self.events.extend(other.events.into_iter());
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
	}

}
