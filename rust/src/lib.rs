//! Laterbase is a bi-temporal event store. This means it has two distinc concepts of time: transaction time and valid time.

use std::{collections::BTreeMap};

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
	/// IDs must be globally unique and orderable. Within them should be
	/// contained a valid physical time, so that clients may generate their
	/// own IDs. 
	/// 
	/// TODO: make sure the physical time is not greater than current time.
	#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
	#[repr(transparent)]
	pub struct Id(ulid::Ulid);

	impl Id {
		pub fn new(ts_ms: u64, rand_bytes: u128) -> Self {
			Self(ulid::Ulid::from_parts(ts_ms, rand_bytes))
		}
	}
}

/// You can send things to an address, and you can store addresses.
trait Address<E>: Clone + Eq + Ord + Sized {
	fn send(&self, msg: Message<E, Self>);
}

/// All of these must be idempotent
#[derive(Clone)]
pub enum Message<E, Addr: Address<E>> {
	Sync(Addr),
	SendEvents {since: time::Transaction<clock::Logical>, dest: Addr},
	StoreEvents {
		from: Option<(Addr, time::Transaction<clock::Logical>)>,
		events: Box<[(event::Id, E)]>,
	}
}

/// At this level an address is just a unique array of bytes
#[derive(Debug)]
pub struct Database<E, Addr: Address<E>> {
	events: BTreeMap<event::Id, E>,
	append_log: Vec<event::Id>,
	version_vector: BTreeMap<Addr, time::Transaction<clock::Logical>>
}

impl<E: Copy, Addr: Address<E>> Database<E, Addr> {
	pub fn new() -> Self {
		Self {
			events: BTreeMap::new(),
			append_log: vec![],
			version_vector: BTreeMap::new(),
		}
	}

	pub fn transaction_logical_clock(
		&self,
		addr: &Addr,
	) -> time::Transaction<clock::Logical> {
		*self
			.version_vector
			.get(addr)
			.unwrap_or(&time::Transaction(clock::LOGICAL_EPOCH))
	}

	pub fn read_events(
		&self,
		time::Transaction(since): time::Transaction<clock::Logical>,
	) -> (Box<[(event::Id, E)]>, time::Transaction<clock::Logical>) {
		let event_ids = &self.append_log[since..];

		let events = event_ids
			.iter()
			.flat_map(|id| {
				self.events.get_key_value(id).map(|(k, &v)| (k.clone(), v))
			})
			.collect();

		(events, time::Transaction(self.append_log.len()))
	}

	pub fn write_events(
		&mut self,
		from: Option<(Addr, time::Transaction<clock::Logical>)>,
		new_events: &[(event::Id, E)],
	) {
		if let Some((addr, lc)) = from {
			self.version_vector.insert(addr, lc);
		}

		for (k, v) in new_events.iter() {
			match self.events.insert(*k, *v) {
				Some(v) => {},
				None => self.append_log.push(*k)
			}
		}
	}
}

trait Receiver<E, Addr: Address<E>> {
	fn recv(
		&mut self,
		src_addr: &Addr,
		incoming_msg: Message<E, Addr>
	);
}

impl<E: Copy, Addr: Address<E>> Receiver<E, Addr> for Database<E, Addr> {
	fn recv(
		&mut self,
		src_addr: &Addr,
		incoming_msg: Message<E, Addr>
	) {
		match incoming_msg {
			Message::Sync(dest) => {
				let since = self.transaction_logical_clock(&dest);
				let (events, lc) = self.read_events(since);
				let from = Some((src_addr.clone(), lc));
				dest.send(Message::StoreEvents{from, events});
			},
			Message::SendEvents {since, dest} => {
				let (events, lc) = self.read_events(since);
				let from = Some((src_addr.clone(), lc));
				dest.send(Message::StoreEvents{from, events});
			},
			Message::StoreEvents { from, events } => {
				self.write_events(from, &events);
			}
		}
	}
}

#[cfg(test)]
mod tests {
	use super::*;
	use proptest::prelude::*;

	#[derive(Debug)]
	struct TestAddress {
		id: [u8; 16],
		db: std::cell::RefCell<Database<u8, Self>>
	}

	impl<'a> Address<u8> for TestAddress {
		fn send(&self, msg: Message<u8, Self>) {
		    self.db.borrow_mut().recv(self, msg);
		}
	}

	impl Clone for TestAddress {
		fn clone(&self) -> Self {
		    Self { id: self.id, db: self.db.clone() }
		}
	}

	impl PartialEq for TestAddress {
		fn eq(&self, other: &Self) -> bool {
		    self.id.eq(&other.id)
		}
	}

	impl Eq for TestAddress {}

	impl PartialOrd for TestAddress {
		fn partial_cmp(&self, other: &Self) -> Option<std::cmp::Ordering> {
		    self.id.partial_cmp(&other.id)
		}
	}

	impl Ord for TestAddress {
		fn cmp(&self, other: &Self) -> std::cmp::Ordering {
		    self.id.cmp(&other.id)
		}
	}

	fn arb_event_id() -> impl Strategy<Value = event::Id> {
		any::<(usize, u128)>()
			.prop_map(|(ts, bytes)| event::Id::new(ts as u64, bytes))
	}

	fn arb_events() -> impl Strategy<Value = Vec<(event::Id, u8)>> {
		prop::collection::vec((arb_event_id(), any::<u8>()), 0..=100)
	}

	fn arb_db() -> impl Strategy<Value = Database<u8, TestAddress>> {
		arb_events().prop_map(|es| {
			let mut db = Database::<u8, TestAddress>::new();
			db.write_events(None, &es);
			db
		})
	}

	fn arb_addr_pair() -> impl Strategy<Value = (TestAddress, TestAddress)> {
		((arb_db(), any::<[u8; 16]>()), (arb_db(), any::<[u8; 16]>()))
		.prop_map(|((db1, id1), (db2, id2))| {
			(
				TestAddress {id: id1, db: std::cell::RefCell::new(db1)},
				TestAddress {id: id2, db: std::cell::RefCell::new(db2)}
			)
		})
	}

	proptest! {
		#[test]
		fn can_read_and_write_events(events in arb_events()) {
			let mut db = Database::<u8, TestAddress>::new();
			db.write_events(None, &events);

			let (recorded_events, _) = 
				db.read_events(time::Transaction(clock::LOGICAL_EPOCH));

			assert_eq!(events.as_slice(), &*recorded_events);
		}

		#[test]
		fn merging_is_idempotent((addr1, addr2) in arb_addr_pair()) {
			addr1.send(Message::Sync(addr2.clone()));
			addr2.send(Message::Sync(addr1.clone()));
		}
	}
}
