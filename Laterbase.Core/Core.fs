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
open System.Threading.Tasks

open NetUlid

(* Convenience functions *)
module Option =
    let ofTry (found, value) = if found then Some value else None

module Seq =
    let equal<'a when 'a : equality> (s1: 'a seq) (s2: 'a seq) = 
        Seq.forall2 (=) s1 s2

module Array =
    let uLength a = (Array.length >> Checked.uint64) a

module Task =
    let iter f t = task {
        let! x = t
        f x
    }

    let map f t = task {
        let! x = t
        return f x
    }

    let bind (f: 'a -> Task<'b>) t = task {
        let! x = t
        return! f x
    }

// Trying to hide it all so I can swap it out later.
type OrderedDict<'k, 'v> private(innerDict: SortedDictionary<'k, 'v>) =
    let seq () = innerDict |> Seq.map (fun kvp -> (kvp.Key, kvp.Value))

    new() = OrderedDict(new SortedDictionary<'k, 'v>())

    member _.Get(k) = innerDict.TryGetValue k |> Option.ofTry

    member self.GetOrDefault(k, defaultValue) =
        self.Get(k) |> Option.defaultValue defaultValue

    member self.GetKeyValue(k) = self.Get(k) |> Option.map (fun v -> (k, v))

    member _.OverWrite(k, v) = innerDict[k] <- v
    member _.TryAdd(k, v) = innerDict.TryAdd(k, v)

    member _.Count with get() = innerDict.Count

    interface IEnumerable<'k * 'v> with
        member _.GetEnumerator(): IEnumerator<_> = seq().GetEnumerator()

        member _.GetEnumerator(): Collections.IEnumerator = 
            seq().GetEnumerator()

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
        this.Id |> Array.map (sprintf "%X") |> String.concat ""

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
    override self.ToString() = self.Ulid.ToString()

[<Struct; IsReadOnly>]
type EventVal<'payload> = {Origin: Address; Payload: 'payload}

type Events<'payload> = (EventID * EventVal<'payload>) array

module Events =
    let view es = Seq.map (fun (k, v) -> (k, v.Origin, v.Payload)) es
    let create addr =
        let toEvents (id, payload) = (id, {Origin = addr; Payload = payload})
        Array.map toEvents

// All of the messages must be idempotent
type Message<'payload> =
    | Sync of destAddr: Address
    | Store of
        events: Events<'payload> *
        fromAddr: Address * 
        numEventsReceived: uint64<received>
    /// This halves the number of events sent across network in simulator
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
    let inTxnOrder<'k, 'v> (eventTable: OrderedDict<'k, 'v>) appendLog since =
        appendLog
        |> Seq.skip (Checked.int since - 1)
        |> Seq.choose eventTable.GetKeyValue

    let execute (eventTable: OrderedDict<'k, 'v>) appendLog query =
        match query.ByTime with
        | PhysicalValid -> eventTable |> Seq.skip (int query.Limit) 
        | LogicalTxn ->
            let since = (uint64 query.Limit) * 1UL<sent>
            inTxnOrder eventTable appendLog since

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
    abstract member Read: query: Query -> Task<Events<'payload>>
    abstract member View: unit -> Task<View<'payload>>
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

/// Idea behind this is I don't have to send a logical clock across network
/// If I store sent and received counts separately, replicas can remember.
/// It made more sense when i thought of it... 
type LogicalClock private (dict:OrderedDict<Address, Counter>) =
    let seq() = Seq.map (fun (k, v) -> (k, v.Sent, v.Received)) dict

    static member Zero = { Sent = 0UL<sent>; Received = 0UL<received> }
    new() = LogicalClock(OrderedDict())

    member _.UpdateSent(fromAddr, numEventsSent) = 
        let newCounter = 
            match dict.Get(fromAddr) with
            | Some counter -> 
                {counter with Sent = max counter.Sent numEventsSent}
            | None -> {Sent = numEventsSent; Received = 0UL<received>}

        dict.OverWrite(fromAddr, newCounter)

    member _.UpdateReceived(fromAddr, numEventsReceived) = 
        let newCounter =
            match dict.Get(fromAddr) with
            | Some counter -> 
                {counter with Received = max counter.Received numEventsReceived}
            | None -> {Sent = 0UL<sent>; Received = numEventsReceived}

        dict.OverWrite(fromAddr, newCounter)

    member _.GetSent(addr) = dict.GetOrDefault(addr, LogicalClock.Zero).Sent
    member _.GetReceived(addr) =
        dict.GetOrDefault(addr, LogicalClock.Zero).Received
    
    interface IEnumerable<Address * uint64<sent> * uint64<received>> with
        member _.GetEnumerator(): IEnumerator<_> = seq().GetEnumerator()
        member _.GetEnumerator(): Collections.IEnumerator = 
            seq().GetEnumerator()

type private LocalReplica<'payload> (addr, sendMsg) =
    let eventTable = OrderedDict<EventID, EventVal<'payload>>()
    (**
        This is linear, and so imposes a total order on a partial order.
        TODO: added another array that keeps track of which were concurrent?
    *)
    let appendLog = ResizeArray<EventID>()
    let logicalClock = LogicalClock()

    // Only add to the append log if the event does not already exist
    let addEvent (k, v: EventVal<'payload>) = 
        if eventTable.TryAdd(k, v) then appendLog.Add k

    member private self.CrashIf(condition, msg) =
        if condition then raise (ReplicaConstraintViolation(msg, self))

    member private self.CheckAppendLog() =
        self.CrashIf(eventTable.Count > appendLog.Count, "Append log is too short")
        self.CrashIf(eventTable.Count < appendLog.Count, "Append log is too long")
    
    interface IReplica<'payload> with
        member val Addr = addr
        member self.Read(query) = 
            self.CheckAppendLog()
            Array.ofSeq (Query.execute eventTable appendLog query)
            |> Task.FromResult

        member self.View() =
            let self = self :> IReplica<'payload> 

            let debug: DebugView = {
                AppendLog = appendLog
                LogicalClock = logicalClock
            }

            task {
                let! es = self.Read {ByTime = PhysicalValid; Limit = 0}
                return { Events = Events.view es; Debug = Some debug }
            }

        member self.Recv (msg: Message<'payload>) =
            match msg with
            | Sync destAddr ->
                let events =
                    logicalClock.GetSent(destAddr)
                    |> Query.inTxnOrder eventTable appendLog
                    |> Seq.toArray

                let numEventsReceived =
                    Checked.uint64 appendLog.Count * 1UL<received>
                    
                Store(events, addr, numEventsReceived) |> sendMsg destAddr
            
            | Store (events, fromAddr, numEventsReceived) ->
                logicalClock.UpdateReceived(fromAddr, numEventsReceived)
                Array.iter addEvent events
                self.CheckAppendLog()

                let numEventsSent = Array.uLength events * 1UL<sent>
                StoreAck(addr, numEventsSent) |> sendMsg fromAddr

            | StoreAck (fromAddr, numEventsSent) ->
                logicalClock.UpdateSent(fromAddr, numEventsSent)
            
            | StoreNew idPayloadPairs ->
                let events = Events.create addr idPayloadPairs 
                Array.iter addEvent events
                self.CheckAppendLog()

let localReplica<'payload> (addr, sendMsg) = 
    LocalReplica(addr, sendMsg) :> IReplica<'payload>