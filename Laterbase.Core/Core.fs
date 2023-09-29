/// Deterministic Core of Laterbase
/// The following is strictly forbidden:
/// - RNG calls
/// - System Time calls
/// - Hashmaps
/// - Multi-threading
module Laterbase.Core

open System
open System.Collections.Generic
open System.Runtime.CompilerServices

open NetUlid

(* Convenience functions *)
module Option =
    let ofTry (found, value) = if found then Some value else None

module Seq =
    let equal<'a when 'a : equality> (s1: 'a seq) (s2: 'a seq) = 
        Seq.forall2 (=) s1 s2

// Trying to hide it all so I can swap it out later.
type OrderedDict<'k, 'v> private(innerDict: SortedDictionary<'k, 'v>) =
    new() = OrderedDict(new SortedDictionary<'k, 'v>())

    member _.Get(k) =  innerDict.TryGetValue k |> Option.ofTry

    member self.GetOrDefault(k, defaultValue) =
        self.Get(k) |> Option.defaultValue defaultValue

    member self.GetKeyValue(k) = 
        self.Get(k) |> Option.map (fun v -> (k, v))

    member _.OverWrite(k, v) = innerDict[k] <- v
    member _.TryAdd(k, v) = innerDict.TryAdd(k, v)

    member _.Count with get() = innerDict.Count

    member _.ToSeq() =
        innerDict |> Seq.map (fun kvp -> (kvp.Key, kvp.Value))

/// When a replica recorded an event
[<Measure>] type transaction

/// When an event happened in the domain
[<Measure>] type valid

[<Measure>] type ms
[<Measure>] type s = Data.UnitSystems.SI.UnitSymbols.s
[<Measure>] type m
[<Measure>] type h
// Using signed ints for this to match .NET stdlib
let msPerS: int64<ms/s> = 1000L<ms/s>
let sPerM: int64<s/m> = 60L<s/m>
let mPerH: int64<m/h> = 60L<m/h>

[<Measure>] type received
[<Measure>] type sent

/// Address in an actor sense. Used for locating Replicas
/// Keeping it as a dumb data type so it's easy to send across a network
[<Struct; IsReadOnly>]
type Address(id: byte array) =
    member _.Id = id
    // Hex string for compactness
    override this.ToString() = 
        this.Id
        |> Array.map (fun b -> b.ToString("X2"))
        |> String.concat ""


/// IDs must be globally unique and orderable. They should contain within
/// them the physical valid time. This is so clients can generate their own
/// IDs.
/// Valid time is used so:
/// - replicas can create their own IDs
/// - we can backdate
/// TODO: make sure the timestamp is not greater than current time.
[<Struct; IsReadOnly>]
type EventID(timestamp: int64<valid ms>, tenRandomBytes: byte array) =
    member _.Ulid = Ulid(int64 timestamp, tenRandomBytes)

[<Struct; IsReadOnly>]
type EventVal<'payload> = {Origin: Address; Payload: 'payload}

type Event<'payload> = EventID * EventVal<'payload>

// All of the messages must be idempotent
type Message<'payload> =
    | Sync of destAddr: Address
    | Store of 
        events: Event<'payload> array *
        fromAddr: Address * 
        numEventsReceived :uint64<received>
    | StoreAck of fromAddr: Address * numEventsSent: uint64<sent>
    | StoreNew of (EventID * 'payload) array

(**
    Read Queries return an event stream. We need to specify
    - the order (logical transaction time, physical valid time)
    - descending or ascending
    - limit (maximum number of events to return)
*)
type Time = PhysicalValid | LogicalTxn

type Query = {
    ByTime: Time
    Limit: int // maximum number of events to return
}

module Query =
    let eventsInTxnOrder<'k, 'v> (events: OrderedDict<'k, 'v>) appendLog since =
        appendLog
        |> Seq.skip (Checked.int since - 1)
        |> Seq.choose events.GetKeyValue

    let execute (events: OrderedDict<'k, 'v>) appendLog query =
        match query.ByTime with
        | PhysicalValid ->
            events.ToSeq() 
            |> Seq.skip (int query.Limit)
        | LogicalTxn ->
            let since = (uint64 query.Limit) * 1UL<sent>
            eventsInTxnOrder events appendLog since

/// For local databases - can see implementation details
type DebugView = {
    AppendLog: EventID seq
    LogicalClock: (Address * uint64<sent> * uint64<received>) seq
}

type View<'payload> = {
    Events: (EventID * Address * 'payload) seq
    Debug: DebugView option
}

type IReplica<'payload> =
    abstract member Addr: Address
    abstract member Read: query: Query -> Event<'payload> seq
    abstract member View: unit -> View<'payload>
    abstract member Recv: Message<'payload> -> unit

/// These are for errors I consider to be un-recoverable.
/// So, why not use an assertion?
/// - Wanted them to crash the program in both prod and dev. (Oppa Tiger Style)
/// - Wanted to catch them in the sim and bring up a GUI to view state
type ReplicaConstraintViolation<'e> (reason: string, replica: IReplica<'e>) =
    inherit Exception (reason)
    member val Replica = replica

/// Double sided counter so we can just send single int across the network
/// TODO save some space and just store the difference?
type Counter = { Sent: uint64<sent>; Received: uint64<received> }

type LogicalClock = OrderedDict<Address, Counter>

/// Idea behind this is I don't have to send a logical clock across network
/// If I store sent and received counts separately, replicas can remember.
/// It made more sense when i thought of it... 
module LogicalClock =
    let zero = { Sent = 0UL<sent>; Received = 0UL<received> }

    let updateSent numEventsSent fromAddr (lc: LogicalClock) = 
        match lc.Get(fromAddr) with
        | Some counter -> 
            {counter with Sent = max counter.Sent numEventsSent}
        | None -> {Sent = numEventsSent; Received = 0UL<received>}

    let updateReceived numEventsReceived fromAddr (lc: LogicalClock) = 
        match lc.Get(fromAddr) with
        | Some counter -> 
            {counter with Received = max counter.Received numEventsReceived}
        | None -> {Sent = 0UL<sent>; Received = numEventsReceived}

    let counter destAddr (lc: LogicalClock) =
        lc.GetOrDefault(destAddr, zero)

type private LocalReplica<'payload> (addr, sendMsg) =
    let events = OrderedDict<EventID, EventVal<'payload>>()
    (**
        This imposes a total order on a partial order.
        Should it be a jagged array with concurrent events stored together? 
    *)
    let appendLog = ResizeArray<EventID>()
    let logicalClock = LogicalClock()

    // Only add to the append log if the event does not already exist
    let addEvent (k, v: EventVal<'payload>) = 
        if events.TryAdd(k, v) then appendLog.Add k

    member private self.CrashIf(condition, msg) =
        if condition then
            raise (ReplicaConstraintViolation(msg, self))

    member private self.CheckAppendLog() =
        self.CrashIf(events.Count > appendLog.Count, "Append log is too short")
        self.CrashIf(events.Count < appendLog.Count, "Append log is too long")
    
    interface IReplica<'payload> with
        member val Addr = addr
        member _.Read(query) = Query.execute events appendLog query

        member self.View() =
            let events = 
                {ByTime = PhysicalValid; Limit = 0}
                |> (self :> IReplica<'payload>).Read
                |> Seq.map (fun (k, v) -> (k, v.Origin, v.Payload))

            let debug: DebugView = {
                AppendLog = appendLog
                LogicalClock =
                    logicalClock.ToSeq()
                    |> Seq.map(fun (k, v) -> (k, v.Sent, v.Received))
            }

            {Events = events; Debug = Some debug}

        member self.Recv (msg: Message<'payload>) =
            match msg with
            | Sync destAddr ->
                let counter = LogicalClock.counter destAddr logicalClock
                let events =
                    Query.eventsInTxnOrder events appendLog (counter.Sent)
                    |> Seq.toArray
                let numEventsReceived = 
                    Checked.uint64 appendLog.Count * 1UL<received>
                let storeMsg = Store(events, addr, numEventsReceived)
                sendMsg destAddr storeMsg
            
            | Store (events, fromAddr, numEventsReceived) ->
                let newCounter = 
                    logicalClock
                    |> LogicalClock.updateReceived numEventsReceived fromAddr 
                    
                logicalClock.OverWrite(fromAddr, newCounter)

                let numEventsBefore = appendLog.Count
                Array.iter addEvent events
                self.CheckAppendLog()

                let numEventsSent = 
                    (appendLog.Count - numEventsBefore)
                    |> Checked.uint64 
                    |> ( * ) 1UL<sent>

                sendMsg fromAddr (StoreAck (addr, numEventsSent))

            | StoreAck (fromAddr, numEventsSent) ->
                let newCounter =
                    logicalClock
                    |> LogicalClock.updateSent numEventsSent fromAddr

                logicalClock.OverWrite(fromAddr, newCounter)
            
            | StoreNew idPayloadPairs ->
                let toEvents (id, payload) = 
                    (id, {Origin = addr; Payload = payload})
                let events = Array.map toEvents idPayloadPairs
                Array.iter addEvent events
                self.CheckAppendLog()

let localReplica<'payload> (addr, sendMsg) = 
    LocalReplica(addr, sendMsg) :> IReplica<'payload>
