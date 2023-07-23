# Laterbase

⚠️ **WORK IN PROGRESS** ⚠️

*The following document describes my end goal. Check 'Roadmap' at the end to se where I'm actually at.*

*Contributors and advice welcome!*

## Product Specifications

### Overview

A fast distributed event store, designed for high write availability even under network partition. 

### Target Users

Users in industries where the domain is naturally eventful. (I'm primarily thinking of supply chain & logistics, but I'm sure there's others). Probably smaller outfits where the clumsiness of traditional ERPs is failing them. Logistics is an even more specific target, as they record more info "in the field" where network resiliency matters.


### Business Objectives:

- Improve supply chain and logistics operational efficiency.
- Function during network outages and in the field. 
- Optimize performance for event stream processing.
- Support multi-platform usage:

### Key Features:

- Backdating
- Multi region deployment without any loss of write availability
- Retroactive event writing for integration of existing and third-party data.
- User-defined schemas and aggregation functions.
- Focus on event sourcing and syncing.

## Functional Specifications

```mermaid
erDiagram
    SERVER ||--|{ DB : accesses
    DB ||--|| EVENT-STREAM : stores
    DB ||--o{ VIEW : has
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

My first instinct was these should be arbitrary bytes, à la embedded key value stores.

This raises a number of questions, however:

- What format should http responses be in? It's one thing to claim arbitrary bytes for the individual events, but some structure has to be imposed to represent an array of them in an http body.

- Is forcing every view function to de-serialize and handle schema changes practical?

#### Aggregate Key

TODO

### Aggregate Value

TODO

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

```

```

GET /db/{db-name}/e?keys={key1, key2}

```

```

Since the keys are ulids, keys are in crockfords base32 text format.

#### Query View

```
GET /{db-name}/{view-name}
```

### Views

So many design decisions...

When should they be added? In what language?

Broadly I wish to utilise CouchDB style map-reduce views over events. 

These 'reduce' over an immutable log of events

```mermaid
graph LR;
    events[e1, e2, e3, ...]
    view
    events --> view
    view --> key1-val1
    view --> key2-val2
    view --> key3-val3
```

## Design specifications

Laterbase should be a library - provide your own code for names of event roots, how to aggregate events, than it spins up a server.

One LMDB env per aggregate root. IE a single LMDB env has an event database as well as an aggregate one.

Modelling the entire database as a grow only set, using delta states.

#### Snapshots

Persist them on read. Reads are fast in LMDB, and we might as well insert on demand.

### FAQ

#### Why not Kafka?

- Multi-master. No clients and servers.

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

- Add pre-compiled views at runtime

- "State machine" stye arbiraries that simulate multiple merges

- Simulate data loss of a node, and syncing again

- Test backdating

- Aggregate snapshot on read

- Sync views??

- Persistent storage using LMDB or similar

- Factor out in-memory storage engine, make a trait

- More tests with tokio-rs turmoil, or whatever works

- ???

- Profit

## References

- Almeida, Paulo Sérgio; Shoker, Ali; Baquero, Carlos (2016-03-04). "Delta State Replicated Data Types". Journal of Parallel and Distributed Computing. 111: 162–173
- Shapiro, Marc; Preguiça, Nuno; Baquero, Carlos; Zawirski, Marek (13 January 2011). "A Comprehensive Study of Convergent and Commutative Replicated Data Types". Rr-7506.
- Douglas Parker, Gerald Popek, Gerard Rudisin, Allen Stoughton, Bruce Walker, Evelyn Walton, Johanna Chow, David Edwards, Stephen Kiser, and Charles Kline. "Detection of mutual inconsistency in distributed systems.". Transactions on Software Engineering. 1983
- Carlos Baquero and Nuno Preguiça. "Why Logical Clocks are Easy". ACM Queue Volume 14, Issue 1. 2016.
