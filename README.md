# Laterbase

⚠️ **WORK IN PROGRESS** ⚠️

*These documents describe my end goal - not where I'm at. Feedback and contributions welcome!*

Laterbase is an append only, multi-master database. It is designed to store and process *events*: immutable facts describing real world events, that took place at a certain (physical) time.

Laterbase supports backdating events.

[Business](business.md)

[Technical](technical.md)

 root. IE a single LMDB env has an event database as well as an aggregate one.

Modelling the entire database as a grow only set, using delta states.

## Roadmap

- ~~G-Set in rust (copy JS version, but make it mutable)~~

- ~~Delta state version. Make sure it passes tests~~

- ~~Sorted version using sequential IDs~~

- Supply timestamps from outside the db, construct UUIDs from those.

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
