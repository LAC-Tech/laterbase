//! Laterbase is a bi-temporal event store. This means it has two distinc concepts of time: transaction time and valid time.

use std;
use std::collections::BTreeMap;

#[cfg(target_endian = "big")]
compile_error!("I have assumed little endian throughout this codebase");

#[cfg(not(target_pointer_width = "64"))]
compile_error!("This code assumes a 64-bit target architecture");

mod time {
	/// When the database recored an event
	#[repr(transparent)]
	#[derive(Clone, Copy, Debug)]
	pub struct Transaction<T>(pub T);

	/// When the event happened in the real world
	#[repr(transparent)]
	#[derive(Clone, Copy)]
	pub struct Valid<T>(T);
}

mod clock {
	pub type Logical = usize;
	pub const LOGICAL_EPOCH: Logical = 0;
	pub type Physical = std::time::SystemTime;
}

mod event {
	/**
	 * IDs must be globally unique and orderable. They should contain within
	 * them the physical valid time. This is so clients can generate their own
	 * IDs.
	 *
	 * TODO: make sure the physical time is not greater than current time.
	 */
	#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
	#[repr(transparent)]
	pub struct Id(ulid::Ulid);

	impl Id {
		pub fn new(ts_ms: u64, rand_bytes: u128) -> Self {
			Self(ulid::Ulid::from_parts(ts_ms, rand_bytes))
		}
	}
}

trait Address<E> {
	//fn send(msg: Message<E>);
}

/** All of these must be idempotent */
enum Message<E> {
	SyncWith(Box<dyn Address<E>>),
	SendEvents {
		since: time::Transaction<clock::Logical>,
		to_addr: Box<dyn Address<E>>,
	},
	StoreEvents {
		from: Option<(Box<dyn Address<E>>, time::Transaction<clock::Logical>)>,
		events: Box<[(event::Id, E)]>,
	},
}

#[derive(Debug)]
pub struct Database<Addr: std::fmt::Debug, E: std::fmt::Debug> {
	events: BTreeMap<event::Id, E>,
	append_log: Vec<event::Id>,
	version_vector: BTreeMap<Addr, time::Transaction<clock::Logical>>,
}

impl<Addr, E> Database<Addr, E> where
	Addr: Ord + Copy + std::fmt::Debug, 
	E: Copy + std::fmt::Debug
{
	fn new() -> Self {
		Self {
			events: BTreeMap::new(),
			append_log: vec![],
			version_vector: BTreeMap::new(),
		}
	}

	fn event_matching_id(&self, id: &event::Id) -> Option<(event::Id, E)> {
		self.events.get_key_value(&id).map(|(&k, &v)| (k, v))
	}

	pub fn transaction_logical_clock(
		&self,
		addr: Addr,
	) -> time::Transaction<clock::Logical> {
		*self
			.version_vector
			.get(&addr)
			.unwrap_or(&time::Transaction(clock::LOGICAL_EPOCH))
	}

	pub fn read_events(
		&self,
		time::Transaction(since): time::Transaction<clock::Logical>,
	) -> (Box<[(event::Id, E)]>, time::Transaction<clock::Logical>) {
		let event_ids = &self.append_log[since..];

		let events: Box<[(event::Id, E)]> = event_ids
			.into_iter()
			.flat_map(|id| self.event_matching_id(id))
			.collect();

		(events, time::Transaction(self.append_log.len()))
	}

	pub fn write_events(
		&mut self,
		from: Option<(Addr, time::Transaction<clock::Logical>)>,
		new_events: Box<[(event::Id, E)]>,
	) {
		if let Some((addr, lc)) = from {
			self.version_vector.insert(addr, lc);
		}

		for (k, v) in new_events.into_iter() {
			self.events.insert(*k, *v);
			self.append_log.push(*k);
		}
	}
}

#[cfg(test)]
mod tests {

use super::*;
	use proptest::prelude::*;

	fn arb_event_id() -> impl Strategy<Value = event::Id> {
		any::<(usize, u128)>()
			.prop_map(|(ts, bytes)| event::Id::new(ts as u64, bytes))
	}

	fn arb_events() -> impl Strategy<Value = Vec<(event::Id, u8)>> {
		prop::collection::vec((arb_event_id(), any::<u8>()), 0..=100)
	}

	proptest! { 
		#[test]
		fn can_read_and_write_events(events in arb_events()) {
			let mut db = Database::<uuid::Uuid, u8>::new();
			db.write_events(None, events.clone().into_boxed_slice());

			let (recorded_events, _) = 
				db.read_events(time::Transaction(clock::LOGICAL_EPOCH));

			assert_eq!(events.as_slice(), &*recorded_events);
		}
	}
}
