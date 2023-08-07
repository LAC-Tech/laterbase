/**
 * Underlying storage engine of a database.
 * This been made into its own trait so it can be sufficiently simulated.
 *
 * Assumptions:
 * 	- this represents an ordered key value store, with multiple 'tables'.
 * 	- only knows about bytes
 */
use std::collections::BTreeMap;

pub trait StorageEngine: PartialEq {
	type Keys<'a>: Iterator<Item = &'a [u8]>
	where
		Self: 'a;

	fn n_events(&self) -> usize;

	// TODO: failure modes for writing
	fn write_event(&mut self, key: &[u8], event: &[u8]);
	fn write_change(&mut self, key: &[u8]);

	fn read_event(&self, key: &[u8]) -> Option<&[u8]>;

	fn update_vector_clock(&mut self, id: &[u8], logical_time: usize);
	fn read_vector_clock(&self, id: &[u8]) -> Option<usize>;

	fn keys_added_since(&self, logical_time: usize) -> Self::Keys<'_>;
}

pub struct Simulated {
	events: BTreeMap<Box<[u8]>, Box<[u8]>>,
	changes: Vec<Box<[u8]>>,
	vector_clock: BTreeMap<Box<[u8]>, usize>,
}

impl Simulated {
	pub fn new() -> Self {
		Self {
			events: BTreeMap::new(),
			changes: vec![],
			vector_clock: BTreeMap::new(),
		}
	}
}

impl StorageEngine for Simulated {
	type Keys<'a> = core::slice::Iter<'a, &'a [u8]>;

	fn n_events(&self) -> usize {
		self.events.len()
	}

	fn write_event(&mut self, key: &[u8], event: &[u8]) {
		self.events.insert(Box::from(key), Box::from(event));
	}

	fn write_change(&mut self, key: &[u8]) {
		self.changes.push(Box::from(key));
	}

	fn read_event(&self, key: &[u8]) -> Option<&[u8]> {
		self.events.get(key).copied()
	}

	fn update_vector_clock(&mut self, id: &[u8], logical_time: usize) {
		self.vector_clock.insert(id, logical_time);
	}

	fn read_vector_clock(&self, id: &[u8]) -> Option<usize> {
		self.vector_clock.get(id).copied()
	}

	fn keys_added_since(&self, logical_time: usize) -> Self::I<'_> {
		self.changes[logical_time..].into_iter()
	}
}

impl PartialEq for Simulated {
	fn eq(&self, other: &Self) -> bool {
		// Events are the source of truth!
		self.events == other.events
	}
}
