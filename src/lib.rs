type NodeID = uuid::Uuid;
type Key = ulid::Ulid;
type Events = std::collections::BTreeMap<Key, Vec<u8>>;

#[derive(Debug)]
#[cfg_attr(test, derive(Clone))]
struct VectorClock(std::collections::HashMap<NodeID, usize>);

impl VectorClock {
	fn new() -> Self {
		Self(std::collections::HashMap::new())
	}

	fn get(&self, node_id: NodeID) -> usize {
		*self.0.get(&node_id).unwrap_or(&0)
	}

	fn update(&mut self, remote_id: NodeID, local_clock: usize) {
		self.0.insert(remote_id, local_clock);
	}
}

struct SyncResponse {
	sending: Events,
	requesting: Vec<Key>
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
		std::collections::BTreeMap<Vec<u8>, Vec<u8>>>
}

impl Node {
	pub fn new() -> Self {
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
		let logical_clock = self.changes.len().saturating_sub(1);
		self.vector_clock.update(remote_id, logical_clock);
	}

	fn added_since_last_sync_with(&self, remote_id: NodeID) -> &[Key] {
		let logical_clock = self.vector_clock.get(remote_id);
		&self.changes[logical_clock..]
	}

	fn sync_handshake(
		&mut self, 
		remote_id: NodeID, 
		remote_keys: &[Key]
	) -> SyncResponse {
		let local_keys = self.added_since_last_sync_with(remote_id);

		let sending: Events = local_keys.iter().filter_map(|local_key| {
			if remote_keys.contains(local_key) {
                return None
            }

			let val = self.events
				.get(local_key)
				.expect("database to be consistent");

			Some((*local_key, val.clone()))

		}).collect();

		let requesting: Vec<Key> = remote_keys.iter().filter_map(|remote_key| {
			if local_keys.contains(remote_key) {
				return None
			}

			Some(*remote_key)
		}).collect();

		SyncResponse { sending, requesting }
	}

	pub fn merge(&mut self, remote: &mut Node) {
		let local_keys = self.added_since_last_sync_with(remote.id);

		let res = remote.sync_handshake(self.id, local_keys);
		self.add_remote(remote.id, res.sending);

		let new_events_for_remote = res.requesting.into_iter().map(|k| {
			let v = self.events.get(&k).expect("database to be consistent").clone();
			(k, v)
		});

		remote.add_remote(self.id, new_events_for_remote.into_iter().collect());
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
    use super::*;
	use proptest::prelude::*;

	const N_BYTES_MAX: usize = 512;
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
			let mut node1 = Node::new();

			for byte_vec in byte_vectors {
		        node1.add_local(byte_vec.as_slice());
		    }

		    let node2 = node1.clone();
		    (node1, node2)
		})
	}

	#[test]
	fn query_empty_vector_clock() {
		let vc = VectorClock::new();
		assert_eq!(vc.get(uuid::Uuid::new_v4()), 0);
	}

	proptest! {
		#[test] 
		fn can_add_and_query_single_element(val in arb_bytes()) {
			let mut node = Node::new();
			let key = node.add_local(val.as_slice());
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
