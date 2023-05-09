# Laterbase

## Product Specifications

### Overview

Laterbase aims to provide a stable and fast foundation for data in the logistics industry.

### Target Users

Software professionals in the logistics space, who need a fast and highly available source of truth.

### Business Objectives

- Allow events to be recorded even with no network present
- Distributed Event Store
- Multi-master: any node is available for writes

## Functional Specifications

Endpoints:

/{db-name}/{aggregate-root}/events
/{db-name}/{aggregate-root}/aggregate

## Design specifications

Using Rust and Axum

Modelling the entire database as a grow only set, using delta states.

multiple event stream/aggregate pairs? one steam with multiple views?

### Why Rust?

- zero overhead calling C libs (probably needed for embedded K/V stores)
- standard library is big and well documented
- healthy ecosystem
- fine-graiend control of memory layout
- kind of functional, which is nice
- tooling is great

### Why not...

#### Zig

- Not 1.0 yet
- No mature web microframework
- Less expressive than rust

### Why Axum?

- Backed by Tokio-rs, which has been around in rust for a long time
- Nicer API than Actix-web
- Makes sense to me!

### Record format

```
[type, version, data ...]
```


## Roadmap


### Phase 1

Tech: rust, axum

In memory only. 
- Easier to test (no dealing with temp files)
- Rapid iteration if I need to test different query patterns
- will need multiple backends anyway


