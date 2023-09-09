//! Laterbase is a bi-temporal event store. This means it has two distinc concepts of time: transaction time and valid time.

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

/// Basically any kind of unique identifier.
#[derive(Debug, PartialEq, PartialOrd, Ord, Eq, Clone)]
pub struct Address(Box<[u8]>);

impl From<&[u8]> for Address {
    fn from(bytes: &[u8]) -> Self {
        Address(bytes.into())
    }
}

impl<const N: usize> From<[u8; N]> for Address {
    fn from(bytes: [u8; N]) -> Self {
        Address(bytes.into())
    }
}

/// All of these must be idempotent
pub enum Message<E> {
	SyncWith(Address),
	SendEvents {
		since: time::Transaction<clock::Logical>,
		remote_addr: Address,
	},
	StoreEvents {
		from: Option<(Address, time::Transaction<clock::Logical>)>,
		events: Box<[(event::Id, E)]>,
	},
}

/// At this level an address is just a unique array of bytes
#[derive(Debug)]
#[cfg_attr(test, derive(Clone))]
pub struct Database<E> {
	events: BTreeMap<event::Id, E>,
	append_log: Vec<event::Id>,
	version_vector: BTreeMap<Address, time::Transaction<clock::Logical>>,
}

impl<E: Copy> Database<E> {
	fn new() -> Self {
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

		let events: Box<[(event::Id, E)]> = event_ids
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

pub trait Ether<E> {
	fn send(&self, msg: Message<E>, addr: Address);
}

#[cfg_attr(test, derive(Clone))]
pub struct Replica<E> {
	addr: Address,
	// Sending messages to an address is late bound.
	// Rerefence counted purely to clone it in tests. not a good reason??
	ether: std::rc::Rc<dyn Ether<E>>,
	db: Database<E>
}

impl<E: Copy> Replica<E> {
	pub fn new(addr: Address, ether: std::rc::Rc<dyn Ether<E>>) -> Self {
		let db = Database::new();
		Self {addr, ether: ether, db}
	}

	pub fn send(&mut self, msg: Message<E>) {
		match msg {
			Message::SendEvents { since, remote_addr } => {
				let (events, t) =self.db.read_events(since);

				let outgoing_msg = Message::StoreEvents {
					from: Some((self.addr.clone(), t)), 
					events
				};

				self.ether.send(outgoing_msg, remote_addr);
			},
			Message::SyncWith(remote_addr) => {
				let outgoing_msg = Message::SendEvents {
					since: self.db.transaction_logical_clock(&remote_addr), 
					remote_addr: self.addr.clone()
				};

				self.ether.send(outgoing_msg, remote_addr);
			},
			Message::StoreEvents { from, events } =>
				self.db.write_events(from, &events)
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

	fn arb_db() -> impl Strategy<Value = Database<u8>> {
		arb_events().prop_map(|es| {
			let mut db = Database::<u8>::new();
			db.write_events(None, &es);
			db
		})
	}

	fn arb_address() -> impl Strategy<Value = Address> {
		any::<[u8; 16]>().prop_map(|bytes| Address::from(bytes))
	}


	struct Ether(BTreeMap<Address, Replica<u8>>);
	
	impl<const N: usize> From<[Replica<u8>; N]> for Ether {
		fn from(replicas: [Replica<u8>; N]) -> Self {
			let tuples = replicas.into_iter().map(|r| (r.addr.clone(), r));
			Self(BTreeMap::from_iter(tuples))
    	}
	}

	impl Ether {
		fn send(&mut self, addr: Address, msg: Message<u8>) {
			let replica = self.0.get_mut(&addr)
				.expect("Haven't tested missing replicas yet");

			replica.send(msg);
		}
	}

	// fn arb_replica() -> impl Strategy<Value = Replica<u8>> {
	// 	arb_db().prop_map(|db| {
	// 		let ether = Ether::from([])
	// 		Replica::new()
	// 	})
	// }

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
		fn merging_is_idempotent(db1 in arb_db(), db2 in arb_db()) {
			let (id1, id2) = (uuid::Uuid::new_v4(), uuid::Uuid::new_v4());

			let id1: Address = Address::from(*uuid::Uuid::new_v4().as_bytes());
			let id2: Address = Address::from(*uuid::Uuid::new_v4().as_bytes());


		
		}
	}
}
