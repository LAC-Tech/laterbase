# Laterbase

⚠️ **WORK IN PROGRESS** ⚠️

*These documents describe my end goal - not where I'm at. Feedback and contributions welcome!*

Laterbase is an event store that syncs. It stores both when an event happened in the real world and when it was received by the data store. Any laterbase can sync with any other laterbase, with guaranteed no loss of data.

[Business](business.md)

[Technical](technical.md)

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
