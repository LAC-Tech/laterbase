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

#[derive(Debug, Clone, Eq, Ord, PartialEq, PartialOrd)]
#[repr(transparent)]
pub struct Address([u8; 16]);

/// All of these must be idempotent
pub enum Message<E> {
	Sync(Address),
	SendEvents {since: time::Transaction<clock::Logical>, dest: Address},
	StoreEvents {
		from: Option<(Address, time::Transaction<clock::Logical>)>,
		events: Box<[(event::Id, E)]>,
	}
}

/// At this level an address is just a unique array of bytes
#[derive(Debug)]
pub struct Database<E> {
	events: BTreeMap<event::Id, E>,
	append_log: Vec<event::Id>,
	version_vector: BTreeMap<Address, time::Transaction<clock::Logical>>
}

impl<E: Copy> Database<E> {
	pub fn new() -> Self {
		Self {
			events: BTreeMap::new(),
			append_log: vec![],
			version_vector: BTreeMap::new(),
		}
	}

	pub fn transaction_logical_clock(
		&self,
		addr: &Address,
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
				self.events.get_key_value(id).map(|(&k, &v)| (k, v))
			})
			.collect();

		(events, time::Transaction(self.append_log.len()))
	}

	pub fn write_events(
		&mut self,
		from: Option<(Address, time::Transaction<clock::Logical>)>,
		new_events: &[(event::Id, E)],
	) {
		if let Some((addr, lc)) = from {
			self.version_vector.insert(addr, lc);
		}

		for (k, v) in new_events.iter() {
			self.events.insert(*k, *v);
			self.append_log.push(*k);
		}
	}
}

#[derive(Debug)]
struct Replica<E> {
	addr: Address,
	db: Database<E>
}

impl<E: Copy> Replica<E> {
	pub fn recv(
		&mut self,
		incoming_msg: Message<E>
	) -> Vec<(Message<E>, Address)> {
		match incoming_msg {
			Message::Sync(dest) => {
				let since = self.db.transaction_logical_clock(&dest);
				let (events, lc) = self.db.read_events(since);
				let from = Some((self.addr.clone(), lc));
				vec![(Message::StoreEvents{from, events}, dest)]
			},
			Message::SendEvents {since, dest} => {
				let (events, lc) = self.db.read_events(since);
				let from = Some((self.addr.clone(), lc));
				vec![((Message::StoreEvents{from, events}, dest))]
			},
			Message::StoreEvents { from, events } => {
				self.db.write_events(from, &events);
				vec![]
			}
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

	fn arb_address() -> impl Strategy<Value = Address> {
		any::<[u8; 16]>().prop_map(Address)
	}

	
	fn arb_db() -> impl Strategy<Value = Database<u8>> {
		arb_events().prop_map(|es| {
			let mut db = Database::<u8>::new();
			db.write_events(None, &es);
			db
		})
	}

	fn arb_replica_pair() -> impl Strategy<Value = (Replica<u8>, Replica<u8>)> {
		(
			(arb_address(), arb_db()),
			(arb_address(), arb_db())
		).prop_map(|((addr1, db1), (addr2, db2))| {
			(Replica {addr: addr1, db: db1}, Replica {addr: addr2, db: db2})

		})
	}

	proptest! {
		#[test]
		fn can_read_and_write_events(events in arb_events()) {
			let mut db = Database::<u8>::new();
			db.write_events(None, &events);

			let (recorded_events, _) = 
				db.read_events(time::Transaction(clock::LOGICAL_EPOCH));

			assert_eq!(events.as_slice(), &*recorded_events);
		}

		#[test]
		fn merging_is_idempotent((mut r1, mut r2) in arb_replica_pair()) {

			let (addr1, addr2) = (r1.addr.clone(), r2.addr.clone());

			let xs = r1.recv(Message::Sync(addr2.clone()));

			for (msg, addr) in xs {
				let mut r = 
					if addr == addr1 {
						r1
					} else if addr == addr2 {
						r2
					} else {
						panic!("lol");
					};
				
				r.recv(msg);
			}

			let xs = &r2.recv(Message::Sync(addr2.clone()));
		}
	}
}