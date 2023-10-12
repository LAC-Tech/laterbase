# Time

> time, mysterious time
> - Sash! featuring Tina Cousins

Laterbase is a bitemporal database; it concerns itself with both transaction time and valid time[^1]. It is also a distributed databases, and so concerns itself with Logical and Physical Time.

This gives us 4 different kinds of time to reason about when it comes to events.

## Event Time

### Physical Transaction Time

The timestamp of when an event was recorded at a replica - from the replicas own clock. This can be shared globally throughout the system - "at what time did at least one replica know about this event?".

### Logical Transaction Time

The causal order in which events were written to a replica. 

As any replica can receive writes at any time and in any order, this value is only useful locally.

### Physical Valid Time

When a user says an event happens. The laterbase policy is to believe them, unless they claim it happened in the future (this also facilites backdating).

### Logical Valid Time

This theoretically exists but is not recorded - I'm yet to find a use for it.

## Hybrid Logical Clocks

HLCs allow us to pack both physical and logical time to a single value.

This would be useful for transaction time, as then the same table could be used for transaction time range queries, and replication.

## References

[^1]: [Snodgrass, Richard. "The Temporal Query Language TQuel."](https://www2.cs.arizona.edu/~rts/pubs/TODS87.pdf)
 