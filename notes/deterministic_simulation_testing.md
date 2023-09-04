# Determinstic Simulation Testing

## SimTigerBeetle (Director's Cut!)

https://www.youtube.com/watch?v=Vch4BWUVzMM

- "How do you test systems for all combinations of failures, when the failures are rare?"
- The state machine, message bus and storage engine are deterministic
- Deterministic storage engine was a big reason they did not choose Rocks or Level
- Run the simulator for a day, you have two years of test time.
- "Can we replay the chaos? Can we share the chaos with our friends and colleagues?"
- As well as foundation DB they were inspired by dropbox w/deterministic fuzzing.
- Try and write as much of the database as you can in a deterministic way.
- "Raft and Paxos - their formal proofs assume that the disk is perfect"
- If you switch on the logs, they will all be printed out in happens-before order.
- Protocol-Aware Testing: Simulator will test the system up to the theoretical limits of the consensus protocol.
## Case Study: TigerBeetle Simulator (VOPR)

commit cbc390cdf94973ade9a6a287b4ea07c8a1c51bc0 
### Main Function

- Determines how many replicas, standbys and clients there will be.
- Seed itself is randomly generated or passed to command line args (presumably to facilitate replays).

### Configuration Options

These are randomly filled in, with the pre-seeded PRNG.
 
Many systems have their own seed - a random number from the top level seeded PRNG. I think this is so each subsystem can be replayed independently.

```mermaid
classDiagram
    class Simulator {
  
    }

    class Cluster {
        replica_count: u8
        standby_count: u8
        client_count: u8
        storage_size_limit: u64
        seed: u64
    }

	class Workload {
		client_count
		in_flight_max
	}

    class Replica {
        crash_probability: float
        crash_stability: uint
        restart_probability: float
        restart_stability: uint
    }

    class Request {
        max: uint
        probability: uint
        idle_on_probability: uint
        idle_off_probability: uint
    }

    class Network {
        node_count: u8
        client_count: u8
        seed: u64
        one_way_delay_mean: u8
        one_way_delay_min: u8
        packet_loss_probability: u8
        path_maximum_capacity: u8
        path_clog_duration_mean: u16
        path_clog_probability: u8
        packet_replay_probability: u8
        partition_probability: u8
        unpartition_probability: u8
        partition_stability: u32
        unpartition_stability: u32
    }

    class Storage {
	    seed: u64
        read_latency: Latency
        write_latency: Latency
        read_fault_probability: u8
	    crash_fault_probability: u8
    }

	class Latency {
		min: u16
		max: u16
	}

	class StorageFaultAtlas {
		faulty_superblock: bool
		faulty_wal_headers: bool
		faulty_wal_prepares: bool
		faulty_client_replies: bool
		faulty_grid: bool
	}

    class StateMachine {
    }

	class PartitionMode{ <<enumeration>> none, uniform_size, uniform_partition, isolate_single }
	class PartitionSymmetry{ <<enumeration>> symmetric, asymmetric }

    Simulator --* Cluster
    Simulator --* Workload
    Simulator --* Replica
    Simulator --* Request
    Cluster --* StorageFaultAtlas
    Cluster --* Network
    Cluster --* Storage
    Cluster --* StateMachine
    Storage --* Latency
    Network --* PartitionMode
    Network --* PartitionSymmetry

```

### Simulation Process

```mermaid
flowchart TD
	Simulator ---> Cluster
	Cluster ---> Network
	Cluster --1:*--> Client
	Cluster --1:*--> Storage
	Cluster --1:*--> ReplicaHealth{Is the replica Up or Down?}
	Client ---> ClientMessageBus[Message Bus]
	Client ---> ClientPingTimeout[Ping Timeout]
	Client ---> RequestTimeout
	ReplicaHealth -->|Up| Replica
	ReplicaHealth -->|Down| ReplicaTimeClock[Advance clock without synchronising]
	Replica --> AdvanceClock
	Replica --> ReplicaPingTimeout[Ping Timeout]
	Replica --> ReplicaMessageBus[Message Bus]
	Replica --> VSR[Various VSR Specific Simulations]
	
```

#### What is being tested?

The high level picture is - various sanity and invariant checks in the form of asserts.

This can happen inside a tick function, as well as the end of the simulation (in the `simulation.done`) method.
#### Glossary

A lot of this VSR related.

- Node: a machine in the cluster. A node can be either a *Replica* or a *Standby*.
- Standby: node currently not participating in the replication.
- Replica: A node participating in the replication
- Primary: The single Replica that is receiving writes.
- Backup: Read-only Replicas
- View: conceptual state snapshots of the whole system
- Storage Fault Atlas - TODO: learn more about storage
- Quorum: There 2f+1 Replicas 
- Core: "strongly-connected component of replicas containing a view change quorum"
- Core vs Quorum

## "Testing Distributed Systems w/ Deterministic Simulation" by Will Wilson

https://www.youtube.com/watch?v=4fFDFbi3toc

- Bugs often not repeatable due to transient network conditions. Packets could be re-arranged or dropped.

- "The messy dirty universe has intruded on our beautiful pristine land of pure functions" - LOL

- Network: Source of entropy & randomness that *you do not control*. Also applies to threads, disks etc.

- "We didn't write a database. We started by writing a simulation of a database. A totally deterministic simulation. And then... we were like 'OK now we can write a database, which is just that but talking to networks and talking to disks for real'"

- For a couple of years foundation DB had no DB, just a simulation.

- You need 'single threaded pseudo-concurrency' - this feels kind of like an actor model crossed with a game loop. But only one real thread or process allowed.

- Need to simulate external sources of randomness. Disks that fail, unreliable networks etc. I do wonder if there's tools out there that do this...

- Simulation needs to be deterministic - ie can't read from actual clock, need to fake it, all rng accesses need to be seeded, etc.

- They made heavy use of 'callbacks' (horrible in C++).

- They **do** use actor model concurrency in the simulation.

- They make use of futures in the simulation. TODO: how are they different from oromises again?

- Their actual C++ implementation is horrifying. Ignoring it for my sanity.

- They already had difference interfaces for network, connections, and async file handling. Their simulation was just another implementation of it. Classic hexagonal architecture!

- They utilise 'test files'. Goals of the system wants to achieve, and simulated obstacles that will stop it achieving those goals.

- They have an invariant they check at the end, ie the 'Ring Test'. K,V pairs, each V points to the next V (V1 -> K2, V2 -> K3). Write a bunch of mutations of those keys and values, and check the ring holds at the end.

- **Random clogging** - prevent network connections sending or receiving packets.

- **Swizzle** - stop network connections on a rolling basis, then bring them back up in reverse order. 

- Kill 7/10 machines, make sure 3 are on at any given time.

- Machines have 1% chance of sending an error.

- Swap storages of two machines, so now a node has a completely different set of data but the same IP address.

- Hardware often fails together, not completely randomly. Try and simulate this ('Hurst exponent').
