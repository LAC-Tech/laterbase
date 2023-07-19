use std::collections::{BTreeMap};

type NodeID = uuid::Uuid;
type Key = ulid::Ulid;
type Events = BTreeMap<Key, Vec<u8>>;

#[derive(Clone, Copy, Debug, PartialEq)]
struct LogicalClock(usize);

#[derive(Debug)]
#[cfg_attr(test, derive(Clone))]
struct VectorClock(std::collections::HashMap<NodeID, LogicalClock>);

impl VectorClock {
	fn new() -> Self {
		Self(std::collections::HashMap::new())
	}

	fn get(&self, node_id: NodeID) -> LogicalClock {
		*self.0.get(&node_id).unwrap_or(&LogicalClock(0))
	}

	fn update(&mut self, remote_id: NodeID, local_clock: LogicalClock) {
		self.0.insert(remote_id, local_clock);
	}
}

type ViewData = BTreeMap<Vec<u8>, Vec<u8>>; 
type ViewFn = fn(LogicalClock, &ViewData, &[u8]) -> ViewData;

#[cfg_attr(test, derive(Clone))]
pub struct View {
	logical_clock: LogicalClock,
	data: ViewData,
	f: ViewFn
}

impl View {
	fn new(f: ViewFn) -> Self {
		let logical_clock = LogicalClock(0);
		let data = ViewData::new();
		Self { logical_clock, data, f }
	}

	fn get(&self, id: &[u8]) -> Option<&[u8]> {
		self.data.get(id).map(|bs| bs.as_slice())
	}
}

impl View {
	fn process(&mut self, event: &[u8]) {
		let new_data = (self.f)(self.logical_clock, &self.data, event);
		self.data.extend(new_data.into_iter());
	}
}

impl std::fmt::Debug for View {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("View")
            .field("btree", &self.data)
            .finish()
    }
}

#[cfg(test)]
mod test {
	use super::*;

	#[derive(serde::Serialize, serde::Deserialize)]
	struct TempReading {
		location: String,
		celcius: f32
	}

	#[test]
	fn can_add_numbers() {
		let events = [
			("a", 20.0),
			("b", 12.0),
			("c", 34.0),
			("a", 13.0),
			("b", -34.0)
		].map(|(location, celcius)| TempReading { 
			location: location.to_string(),
			celcius
		});
	
		let mut running_average = View::new(|n_events, accum, event| {
			let mut result = ViewData::new();
			let event: TempReading = bincode::deserialize(event).unwrap();
			
			let id = bincode::serialize(&event.location).unwrap();
			let n = accum.get(&id)
				.map(|id| id.as_slice())
				.map(|existing_average| {
					let existing_average: f32 = 
						bincode::deserialize(existing_average).unwrap();
					
					let n_events = n_events.0 as f32;

					(existing_average + event.celcius) / n_events
				})
				.unwrap_or(event.celcius);

			let val = bincode::serialize(&n).unwrap();

			result.insert(id, val);

			result
		});

		for e in events {
			let e = bincode::serialize(&e).unwrap();
			running_average.process(e.as_slice());
		}

		let id = bincode::serialize("a").unwrap();
		let expected = bincode::serialize(&(16.5 as f32)).unwrap();

		dbg!(&running_average);

		assert_eq!(
			running_average.get(id.as_slice()), 
			Some(expected.as_slice()));
	}
}

#[derive(Debug)]
#[cfg_attr(test, derive(Clone))]
pub struct Node {
	id: NodeID,
	events: Events,
	changes: Vec<Key>,
	vector_clock: VectorClock,
	views: std::collections::HashMap<
		String,
		std::collections::BTreeMap<String, View>>
}

impl Node {
	pub fn new(views: Vec<View>) -> Self {
		let id = uuid::Uuid::new_v4();
		let events = Events::new();
		let changes = vec![];
		let vector_clock = VectorClock::new();
		let views = std::collections::HashMap::new();
		Self {id, events, changes, vector_clock, views}
	}

	pub fn add_local(&mut self, v: &[u8]) -> Key {
		let k = ulid::Ulid::new();
		self.events.insert(k, v.to_vec());
		self.changes.push(k);
		k
	}

	pub fn get(&self, k: &Key) -> Option<&[u8]> {
		self.events.get(k).map(|v| v.as_slice())
	}

	fn add_remote(&mut self, remote_id: NodeID, remote_events: Events) {
		self.changes.extend(remote_events.keys());
		self.events.extend(remote_events);
		let logical_clock = LogicalClock(self.changes.len().saturating_sub(1));
		self.vector_clock.update(remote_id, logical_clock);
	}

	fn added_since_last_sync_with(&self, remote_id: NodeID) -> &[Key] {
		let logical_clock = self.vector_clock.get(remote_id);
		&self.changes[logical_clock.0..]
	}

	fn unrecorded_events(
		&self,
		local_keys: &[Key],
		remote: &Self,
		remote_keys: &[Key]
	) -> Events {
		remote_keys.iter().filter_map(|remote_key| {
			if local_keys.contains(remote_key) {
                return None
            }
			let val = remote.events
				.get(remote_key)
				.expect("database to be consistent");

			Some((*remote_key, val.clone()))
		})
		.collect()
	}

	pub fn merge(&mut self, remote: &mut Node) {
		let local_keys = self.added_since_last_sync_with(remote.id).to_vec();
		let remote_keys = remote.added_since_last_sync_with(self.id).to_vec();
		
		let new_local_events =
			self.unrecorded_events(&local_keys, remote, &remote_keys);
		self.add_remote(remote.id, new_local_events);

		let new_remote_events = 
			self.unrecorded_events(&remote_keys, self, &local_keys);		
		remote.add_remote(self.id, new_remote_events);
	}
}

impl PartialEq for Node {
	fn eq(&self, other: &Self) -> bool {
    	// Events are the source of truth!
        self.events == other.events
    }
}

// Equality of event streams is reflexive
impl Eq for Node {}

#[cfg(test)]
mod tests {
	use pretty_assertions::{assert_eq};

    use super::*;
	use proptest::prelude::*;

	const N_BYTES_MAX: usize = 2;
	const N_VALS_MAX: usize = 8;

	fn arb_bytes() -> impl Strategy<Value = Vec<u8>> {
        prop::collection::vec(any::<u8>(), 0..=N_BYTES_MAX)
	}

	fn arb_byte_vectors() -> impl Strategy<Value = Vec<Vec<u8>>> {
		prop::collection::vec(arb_bytes(), 0..=N_VALS_MAX)
	}

	/*
		Generate identical pairs of databases, that can be independently
		mutated to prove algebraic properties of CRDTs
	*/
	fn arb_db_pairs() -> impl Strategy<Value = (Node, Node)> {
		arb_byte_vectors().prop_map(|byte_vectors| {
			let mut node1 = Node::new(vec![]);

			for byte_vec in byte_vectors {
				node1.add_local(&byte_vec);
			}

			let node2 = node1.clone();

		    (node1, node2)
		})
	}

	#[test]
	fn query_empty_vector_clock() {
		let vc = VectorClock::new();
		assert_eq!(vc.get(uuid::Uuid::new_v4()), LogicalClock(0));
	}

	proptest! {
		#[test]
		fn can_add_and_query_single_element(val in arb_bytes()) {
			let mut node = Node::new(vec![]);
			let key = node.add_local(&val);
			assert_eq!(node.get(&key), Some(val.clone().as_slice()))
		}

		#[test]
		fn idempotent((mut node1, mut node2) in arb_db_pairs()) {
			node1.merge(&mut node2);
			assert_eq!(node1, node2);
		}

		// (a . b) . c = a . (b . c)
		#[test]
		fn associative(
			(mut db_left_a, mut db_right_a) in arb_db_pairs(), 
			(mut db_left_b, mut db_right_b) in arb_db_pairs(),
			(mut db_left_c, mut db_right_c) in arb_db_pairs()
		) {
			db_left_a.merge(&mut db_left_b);
			db_left_a.merge(&mut db_left_c);

			db_right_b.merge(&mut db_right_c);
			db_right_a.merge(&mut db_right_b);

			assert_eq!(db_left_a, db_right_a);
		}
		
		// a . b = b . a
		#[test]
		fn commutative(
			(mut db_left_a, mut db_right_a) in arb_db_pairs(), 
			(mut db_left_b, mut db_right_b) in arb_db_pairs(),
		) {
			db_left_a.merge(&mut db_left_b);
			db_right_b.merge(&mut db_right_a);

			assert_eq!(db_left_a, db_right_b);
		}
	}
}
