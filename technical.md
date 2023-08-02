# Laterbase Technical Doc

The whole database is a [CRDT](https://crdt.tech/) - specifically, a grow-only map.

```mermaid
erDiagram
    SERVER ||--|{ DB : accesses
    DB ||--|{ EVENT-STREAM : stores
```

## Core On Disk Format

The whole database is a [CRDT](https://crdt.tech/) - specifically, a grow-only map.

This maps well to ordered key value stores, like LMDB or IndexedDB.

## Sync Protocol

This is my take on a delta-state CRDT.

```mermaid
sequenceDiagram
    participant Local
    participant Remote
    Local->>Remote: IDs added since last sync
    Remote->>Local: Subset of those IDs remote doesn't have, IDs added since last sync.
    Local->>Remote: Events matching those ID's. Subset of IDs local doesn't have.
    Remote->>Local: Events matching those ID's.
```

### Data Formats

#### Event Key

Each key needs to be

- unique, so you can never get id conflicts when syncing from other nodes

- sortable, so you have an order when computing aggregates

Using ULIDs. Considered hybrid logical clocks but I don't need to capture causality in my ids.

Also considered UUIDv7s but the rust package situation was slightly more flakey. Should probably revisit this decision on the actual merits.

##### Hybrid Logical Clocks: Reconsidered

- Can query events in relation to physical time

- Is always close to an NTP clock (standard 64 bit unix timestamp??)

- Causality: e hb f => hlc.e < hlc.f, lc.e = lc.f => e || f, e hb f <=> vc.e < vc.f

- Does not require a server-client architecture: which is good because I don't have one!

- Works for a peer to peer node setup.

- Monotonic, unlike NTP

- Can "identify consistent snapshots in distributed databases". Unique indentifier??

- "The goal of HLC is to provide one-way causality detection similar to that provided by LC, while maintaining the clock value to be always close to the physical/NTP clock."

Are they unique? I don't think so.
What problem would they solve for me?

#### Event Value

Arbitrary bytes.

### HTTP Endpoints

#### Write one or more events

```
POST /db/{db-name}/e
```

#### Event changes feed

```
GET /db/{db-name}/e?lc={logical_clock}
```

Gets all the events a node *doesn't know about*.

This is not the same as getting all events that have happened since a certain time, since it's possible to backdate events. They are however returned in order of their hybrid logical clocks.

#### Bulk read arbitrary events

GET /db/{db-name}/e?keys={key1, key2}

Since the keys are ulids, keys are in crockfords base32 text format.

#### Query View

```
GET /{db-name}/{view-name}
```

### Views



## Design specifications

Laterbase should be a library - provide your own code for names of event roots, how to aggregate events, than it spins up a server.

One LMDB env per aggregate root. IE a single LMDB env has an event database as well as an aggregate one.

Modelling the entire database as a grow only set, using delta states.

### FAQ

#### Why not Kafka?

- Has clients and servers - I want to do something multi-master.

- Streams events are not designed for querying directly, that's what views are for.

#### Why LMDB?

Not 100% that LMDB should be the server side backing store. But I like it because...

- simple and does one thing. Less to learn/remember
- stable
- well documented
- easy to build
- fast reads

#### Why not LMDB?

- Theoretically an LSM might be better for fast write speeds. TODO: actually measure this.
- Only one writer at a time

#### Why Rust?

- zero overhead calling C libs (probably needed for embedded K/V stores)
- standard library is big and well documented
- healthy ecosystem
- fine-grained control of memory layout
- kind of functional, which is nice
- tooling is great

#### Why not Zig?

- Not 1.0 yet
- No mature web micro-framework
- Less expressive than rust

#### Why Axum?

- Backed by Tokio-rs, which has been around in rust for a long time
- Nicer API than Actix-web
- Makes sense to me!

## Roadmap

- ~~G-Set in rust (copy JS version, but make it mutable)~~

- ~~Delta state version. Make sure it passes tests~~

- ~~Sorted version using sequential IDs~~

- HTTP Server
  
  - ~~Create new DB with POST request~~
  
  - ~~Factor out DB into its own file~~
  
  - ~~Create DB, write events, read events back~~
  
  - replicate tests in db module

- Add pre-compiled views at runtime

- "State machine" stye arbiraries that simulate multiple merges

- Simulate data loss of a node, and syncing again

- Test backdating

- Aggregate snapshot on read

- Sync views??

- Persistent storage using LMDB or similar

- Factor out in-memory storage engine, make a trait

- More tests with tokio-rs turmoil (recommended on discord), or whatever works

- ???

- Profit

## References

- Almeida, Paulo Sérgio; Shoker, Ali; Baquero, Carlos (2016-03-04). "Delta State Replicated Data Types". Journal of Parallel and Distributed Computing. 111: 162–173
- Shapiro, Marc; Preguiça, Nuno; Baquero, Carlos; Zawirski, Marek (13 January 2011). "A Comprehensive Study of Convergent and Commutative Replicated Data Types". Rr-7506.
- Douglas Parker, Gerald Popek, Gerard Rudisin, Allen Stoughton, Bruce Walker, Evelyn Walton, Johanna Chow, David Edwards, Stephen Kiser, and Charles Kline. "Detection of mutual inconsistency in distributed systems.". Transactions on Software Engineering. 1983
- Carlos Baquero and Nuno Preguiça. "Why Logical Clocks are Easy". ACM Queue Volume 14, Issue 1. 2016.
