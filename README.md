# Laterbase

⚠️ **WORK IN PROGRESS** ⚠️

*These documents describe my end goal - not where I'm at. Feedback and contributions welcome!*

Laterbase is distributed event store. It stores both when an event happened in the real world and when it was received by the data store.

A laterbase replica can be written to without any coordination with other replicas, can keep working even in the face of a network outage. When the network reconnects, it can then sync. If any two laterbase replicas have received the same updates - in any order - they will be in the same state.

The intended usecase for this is in industries where the domain model is naturally eventful, and where data needs to be collected either in the field, or with very low write latency.

[Business](notes/business.md)

[Technical](notes/technical.md)

## Roadmap

- ~~G-Set in F# (copy JS version, but make it mutable)~~

- ~~Delta state version. Make sure it passes tests~~

- ~~Sorted version using sequential IDs~~

- ~~Give replicas an actor model type interface~~

- ~~Use property based testing to prove merging is idempotent, associative and commutative~~

- ~~Basic graphical inspector~~

- ~~Basic Deterministic Simulation Tester~~

- Simualate network latencies

- Simulate data loss of a node, and recovery

- Test backdating

- Test forward dating is forbidden

- Persistent storage using LMDB, WiredTiger, or something else.

- HTTP Server
