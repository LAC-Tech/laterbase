﻿module Laterbase.Core

open System
open System.Collections.Generic
open System.Linq
open System.Runtime.CompilerServices

open NetUlid

(* Convenience functions *)
module Option =
    let ofTry (found, value) = if found then Some value else None

module Seq =
    let equal<'a when 'a : equality> (s1: 'a seq) (s2: 'a seq) = 
        Seq.forall2 (=) s1 s2

let inline flip f y x = f x y

module Dict =
    let get k (dict: SortedDictionary<'k, 'v>) = 
        dict.TryGetValue k |> Option.ofTry
    
    let getOrDefault k defaultValue (dict: SortedDictionary<'k, 'v>)  = 
        get k dict |> Option.defaultValue defaultValue
    
    let getKeyValue k dict = get k dict |> Option.map (fun v -> (k, v))

    let toSeq (dict: SortedDictionary<'k, 'v>) =
        dict |> Seq.map (fun kvp -> (kvp.Key, kvp.Value))

/// When a replica recorded an event
[<Measure>] type transaction

/// When an event happened in the domain
[<Measure>] type valid

open FSharp.Data.UnitSystems.SI.UnitSymbols
[<Measure>] type ms
[<Measure>] type m
[<Measure>] type h
// Using signed ints for this to match .NET stdlib
let msPerS: int64<ms/s> = 1000L<ms/s>
let sPerM: int64<s/m> = 60L<s/m>
let mPerH: int64<m/h> = 60L<m/h>

[<Measure>] type events
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

module Event =
    /// IDs must be globally unique and orderable. They should contain within
    /// them the physical valid time. This is so clients can generate their own
    /// IDs.
    /// Valid time is used so:
    /// - replicas can create their own IDs
    /// - we can backdate
    /// TODO: make sure the timestamp is not greater than current time.
    type ID = Ulid

    let newId (timestamp: int64<valid ms>) (randomness: byte array) =
        Ulid(int64 timestamp, randomness)

    [<Struct; IsReadOnly>]
    type Val<'payload> = {Origin: Address; Payload: 'payload}

    let newVal addr payload = {Origin = addr; Payload = payload }

    type Stream<'payload> = (ID * Val<'payload>) seq

// All of the messages must be idempotent
[<Struct; IsReadOnly>]
type Message<'payload> =
    | Sync of destAddr: Address
    | Store of 
        events: (Event.ID * Event.Val<'payload>) list *
        from: (Address * uint64<received events>)
    | StoreNew of (Event.ID * 'payload) list

type LogicalClock() =
    // Double sided counter so we can just send single counters across the network
    // TODO save some space and just store the difference?
    member val Sent = 
        SortedDictionary<Address, uint64<sent events>>()
    member val Received = 
        SortedDictionary<Address, uint64<received events>>()

    // TODO: move out of here
    member self.View() =
        let dt = new Data.DataTable()
        dt.Columns.Add "Address" |> ignore
        dt.Columns.Add "Sent" |> ignore
        dt.Columns.Add "Received" |> ignore

        self.Sent.Join(
            self.Received, 
            (fun kvp -> kvp.Key),
            (fun kvp -> kvp.Key),
            (fun sent received -> 
                (sent.Key, sent.Value, received.Value)))

    override self.ToString() =
        // Parker 1983 syntax
        let stringify (dict: IDictionary<_, _>) =
            let elems = 
                [for v in dict -> $"{v.Key}:{v.Value}" ]
                |> String.concat ","
            $"<{elems}>"

        [
            $"└ sent = {stringify self.Sent}"; 
            $"└ received = {stringify self.Received}"
        ] |> String.concat "\n"

type Database<'payload>() =
    member val internal Events = 
        SortedDictionary<Event.ID, Event.Val<'payload>>()
    (**
        This imposes a total order on a partial order.
        Should it be a jagged array with concurrent events stored together? 
    *)
    member val internal AppendLog = ResizeArray<Event.ID>()
    member val internal LogicalClock = LogicalClock()

    member self.ReadEventCount() =
        Checked.uint64 self.AppendLog.Count * 1UL<events received>

    member self.ReadEventsInTxnOrder (since: uint64<sent events>) =
        self.AppendLog 
        |> Seq.skip (Checked.int since - 1) 
        |> Seq.choose (flip Dict.getKeyValue self.Events)

    member self.ReadEventCountFrom(destAddr) = 
        Dict.getOrDefault destAddr 0UL<events sent> self.LogicalClock.Sent 

    /// TODO: better name? 
    member self.UpdateLogicalClock(addr, numEventsReceived) = 
        self.LogicalClock.Received[addr] <- numEventsReceived

    member self.WriteEvents newEvents =
        for (k, v) in newEvents do
            if self.Events.TryAdd(k, v) then
                self.AppendLog.Add k

        if (self.Events.Count > self.AppendLog.Count) then
            Error "Append log is too short"
        elif (self.Events.Count < self.AppendLog.Count) then
            Error "Append log is too long"
        else
            Ok ()

    override self.ToString() =
        let es = 
            [for e in self.Events -> $"{e.Key}, {e.Value}" ]
            |> String.concat "; "

        let appendLogStr =
            [for id in self.AppendLog -> $"{id}" ]
            |> String.concat "; "

        $"Database\n└ events = [{es}]\n└ log = [{appendLogStr}]\n{self.LogicalClock}"

(**
    Read Queries return an event stream. We need to specify
    - the order (logical transaction time, physical valid time)
    - descending or ascending
    - limit (maximum number of events to return)
*)

type Time = PhysicalValid | LogicalTxn

type ReadQuery = {
    ByTime: Time
    Limit: byte // maximum number of events to return
}

module Replica =
    /// For local databases - can see implementation details
    type DebugView = {
        AppendLog: Event.ID seq
        LogicalClock: 
            (Address * uint64<events sent> * uint64<events received>) seq
    }

/// This is meant to be used by client code.
/// Think pouchDBs db class, where whether it's local or remote is abstracted
/// TODO: return Tasks
type IReplica<'e> =
    abstract member Addr: Address
    abstract member Read: query: ReadQuery -> Event.Stream<'e>
    abstract member Debug: unit -> Replica.DebugView option
    /// Replicas *receive* a message that is *sent* across some medium
    abstract member Recv: Message<'e> -> unit

exception MyError of string

type ConstraintViolation<'e> (reason: string, replica: IReplica<'e>) =
    inherit Exception (reason)
    member val Replica = replica

type LocalReplica<'e>(addr, sendMsg) =
    let db = Database<'e>()

    member private self.ThrowIfErr<'e>(res) =
        match res with
        | Ok x -> x
        | Error msg -> raise (ConstraintViolation<'e>(msg, self))
    
    interface IReplica<'e> with
        member val Addr = addr
        member _.Read(query) = 
            match query.ByTime with
            | PhysicalValid -> 
                db.Events |> Dict.toSeq |> Seq.take (int query.Limit)
            | LogicalTxn ->
                let since = (uint64 query.Limit) * 1UL<sent events>
                db.ReadEventsInTxnOrder(since)

        member _.Debug() = Some {
            AppendLog = db.AppendLog
            LogicalClock = db.LogicalClock.View()
        }

        member self.Recv (msg: Message<'e>) =
            match msg with
            | Sync destAddr ->
                let since = db.ReadEventCountFrom destAddr
                let events = db.ReadEventsInTxnOrder since
                let lc = db.ReadEventCount()
                let storeMsg = Store (List.ofSeq events, (addr, lc))
                sendMsg destAddr storeMsg
            | Store (events, from) ->
                // If from another replica, update logical clock to reflect this
                db.UpdateLogicalClock from
                db.WriteEvents events |> self.ThrowIfErr
            | StoreNew idPayloadPairs ->
                let toEvents (id, payload) = (id, Event.newVal addr payload)
                idPayloadPairs
                |> List.map toEvents
                |> db.WriteEvents
                |> self.ThrowIfErr
