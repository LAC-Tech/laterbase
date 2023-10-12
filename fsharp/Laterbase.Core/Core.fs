/// <summary>Deterministic Core of Laterbase</summary>
/// The following is strictly forbidden:
///
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

module Array =
    let uLength a = (Array.length >> Checked.uint64) a

(*
    Don't yet know enough but Task or Async, and don't want to tie myself down to their semantics.

    In the future I may implement the 'non-immediate' version with those things, or maybe something else entirely. Who knows.

    "We all have a Future" - Erik Meijer
*)
type Future<'a> = 
    | Immediate of 'a
    // TODO: actual concurrent case

module Future =
    let iter (f: 'a -> unit) (Immediate x) = f x
    let map f (Immediate x) = Immediate (f x)
    let bind (f: 'a -> Future<'b>) (Immediate x) = f x
    let all<'a> (fs: Future<'a> seq) = 
        Seq.map (fun (Immediate x) -> x) fs |> Immediate

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

[<Measure>] type counter // txn logical time

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
///
/// Valid time is used so:
/// - replicas can create their own IDs
/// - we can backdate
///
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
    | Sync of destAddr: Address * lastCounter: uint64<counter>
    | Store of
        events: Events<'payload> *
        fromAddr: Address * 
        newCounter: uint64<counter>
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

/// For local databases - can see implementation details
type DebugView = {
    AppendLog: EventID seq
    LogicalClock: (Address * uint64<counter>) seq
}

type View<'payload> = {
    Events: (EventID * Address * 'payload) seq
    Debug: DebugView option
}

type IReplica<'payload> =
    abstract member Addr: Address
    abstract member Read: query: Query -> Future<Events<'payload>>
    (* abstract member View: unit -> Future<View<'payload>> *)
    abstract member Recv: Message<'payload> -> unit
    abstract member Count: uint64<counter>

/// These are for errors I consider to be un-recoverable.
/// So, why not use an assertion?
/// - Wanted them to crash the program in both prod and dev. (Oppa Tiger Style)
/// - Wanted to catch them in the sim and bring up a GUI to view state
type ReplicaConstraintViolation<'e> (reason: string, replica: IReplica<'e>) =
    inherit Exception (reason)
    member val Replica = replica

/// Idea behind this is I don't have to send a logical clock across network
/// If I store sent and received counts separately, replicas can remember.
/// It made more sense when i thought of it... 
type LogicalClock private (dict:OrderedDict<Address, uint64<counter>>) =
    let seq() = Seq.map (fun (k, v) -> (k, v)) dict

    new() = LogicalClock(OrderedDict())

    member _.Update(fromAddr, newCounter) = 
        let newCounter = 
            match dict.Get(fromAddr) with
            | Some counter -> max counter newCounter
            | None -> 0UL<counter>

        dict.OverWrite(fromAddr, newCounter)

    member _.Get(addr) = dict.GetOrDefault(addr, 0UL<counter>)
    
    interface IEnumerable<Address * uint64<counter>> with
        member _.GetEnumerator(): IEnumerator<_> = seq().GetEnumerator()
        member _.GetEnumerator(): Collections.IEnumerator = 
            seq().GetEnumerator()

type private LocalReplica<'payload> (addr, sendMsg) =
    (**
        This is linear, and so imposes a total order on a partial order.
        TODO: added another array that keeps track of which were concurrent?
    *)
    let appendLog = ResizeArray<EventID * EventVal<'payload>>()
    let logicalClock = LogicalClock()

    let txnOrder since = Seq.skip since appendLog

    let addEvent (k, v) = appendLog.Add(k, v)
    
    interface IReplica<'payload> with
        member val Addr = addr
        member _.Count with get() = 
            appendLog.Count |> Checked.uint64 |> ( * ) 1UL<counter>
        member self.Read(query) = 
            let seq = match query.ByTime with
            | LogicalTxn -> txnOrder (query.Limit - 1)
            // Very naive but only used in inspector
            | PhysicalValid -> 
                let idSorted = OrderedDict()
                for (k, v) in appendLog do
                    if not (idSorted.TryAdd(k, v)) then
                        failwith "duplicate events in log"

                idSorted |> Seq.skip query.Limit

            seq |> Seq.toArray |> Immediate
        (*
        member self.View() =
            let self = self :> IReplica<'payload> 

            let debug: DebugView = {
                AppendLog = appendLog
                LogicalClock = logicalClock
            }

            self.Read {ByTime = PhysicalValid; Limit = 0}
            |> Future.map(fun es -> {
                Events = Events.view es;
                Debug = Some debug
            })
        *)

        member self.Recv (msg: Message<'payload>) =
            match msg with
            | Sync (destAddr, lastCounter) ->
                let since = logicalClock.Get(destAddr) |> Checked.int
                let events = txnOrder since |> Seq.toArray

                let newCounter = Checked.uint64 appendLog.Count * 1UL<counter>
                    
                Store(events, addr, newCounter) |> sendMsg destAddr
            
            | Store (events, fromAddr, newCounter) ->
                logicalClock.Update(fromAddr, newCounter)
                events 
                |> Seq.filter (fun (_, v) -> v.Origin <> addr)
                |> Seq.iter addEvent
            
            | StoreNew idPayloadPairs ->
                let events = Events.create addr idPayloadPairs
                Array.iter addEvent events

let localReplica<'payload> (addr, sendMsg) = 
    LocalReplica(addr, sendMsg) :> IReplica<'payload>
