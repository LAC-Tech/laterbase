/**
 * Underlying storage engine of a database.
 * This been made into its own trait so it can be sufficiently simulated.
 *
 * Assumptions:
 * 	- this represents an ordered key value store, with multiple 'tables'.
 * 	- only knows about bytes
 */
use std::collections::BTreeMap;

type Keys<'a> = Box<dyn Iterator<Item = &'a [u8]> + 'a>;

pub trait Storage: PartialEq {
	const NAME: &'static str;

	fn n_events(&self) -> usize;

	// TODO: failure modes for writing
	fn write_event(&mut self, key: &[u8], event: &[u8]);
	fn write_change(&mut self, key: &[u8]);

	fn read_event(&self, key: &[u8]) -> Option<&[u8]>;

	fn update_vector_clock(&mut self, id: &[u8], logical_time: usize);
	fn read_vector_clock(&self, id: &[u8]) -> Option<usize>;

	fn keys_added_since(&self, logical_time: usize) -> Keys<'_>;
}

#[cfg_attr(test, derive(Clone, Debug))]
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

impl Storage for Simulated {
	const NAME: &'static str = "Simualted In-Memory Storage";

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
		self.events.get(key).map(|event| &**event)
	}

	fn update_vector_clock(&mut self, id: &[u8], logical_time: usize) {
		self.vector_clock.insert(id.into(), logical_time);
	}

	fn read_vector_clock(&self, id: &[u8]) -> Option<usize> {
		self.vector_clock.get(id).copied()
	}

	fn keys_added_since(&self, logical_time: usize) -> Keys<'_> {
		Box::from(self.changes[logical_time..].iter().map(|key| &**key))
	}
}

impl PartialEq for Simulated {
	fn eq(&self, other: &Self) -> bool {
		// Events are the source of truth!
		self.events == other.events
	}
}
