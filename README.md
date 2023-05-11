# Laterbase

## Product Specifications

### Overview

A fast, highly available event store, for use as a foundational data store for industires like supply chain.

### Target Users

Users in industrys where the domain is naturally eventful. (I'm primarily thinking of supply chain & logistics, but I'm sure there's others). Probably smaller outfits where the clumsiness of traditional ERPs is failing them. Logistics is probably an even more specific target, as they record more info "in the field" where network resiliency matters.

### Business Objectives

- Can handle network outages; allow events to be recorded locally and synced later
- Allow back-dating
- High performance - specifically designed for event sourcing
- Extensible: users provide their own schemas and aggregation functions

## Functional Specifications

### HTTP Endpoints

#### Write one or more events
```
POST /e/{aggregate-root}
```

#### Read all aggregates (paginated)
```
GET /a/{aggregate-root}
```

#### Read single aggregate
```
GET /a/{aggregate-root}/{aggregate-id}
```

#### Changes feed
```
GET /e/{aggregate-root}?vv={version-vector} 
```

Gets all the events the user *doesn't know about*.

This is not the same as getting all events that have happened since a certain time, since it's possible to backdate events. They are however returned in order of their hybrid logical clocks.

### Read model

Completely out of scope! Plus CQRS and all that.

## Design specifications

Laterbase should be a library - provide your own code for names of event roots, how to aggregate events, than it spins up a server.

Modelling the entire database as a grow only set, using delta states.
Each aggregate would be a different database in LMDB. Or would it be an ENV? LMDB only has one writer per env.

### Why LMDB?

To clarify, not 100% that LMDB should be the server side backing store. But I like it because...

- simple and does one thing. less to learn/remember
- stable
- well documented
- easy to build
- fast reads

### Why not LMDB?

- Theoretically an LSM might be better for fast write speeds. TODO: actually measure this.
- Only one writer at a time

### Why Rust?

- zero overhead calling C libs (probably needed for embedded K/V stores)
- standard library is big and well documented
- healthy ecosystem
- fine-graiend control of memory layout
- kind of functional, which is nice
- tooling is great

### Why not Zig?

- Not 1.0 yet
- No mature web microframework
- Less expressive than rust

### Why Axum?

- Backed by Tokio-rs, which has been around in rust for a long time
- Nicer API than Actix-web
- Makes sense to me!

### Record format

#### Event

```
[type, version, data ...]
```

#### Aggregate

Custom

## Roadmap

- G-Set in rust (copy JS version, but make it mutable)
- Delta state version. Make sure it passes tests.
- Sorted version using hybrid logical clocks
- Test backdating

