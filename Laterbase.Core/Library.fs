module Laterbase.Core

open System
open System.Collections.Generic
open System.Linq
open System.Runtime.CompilerServices

open NetUlid

(* Convenience functions *)
module Option =
    let ofTry (found, value) = if found then Some value else None

let dictGet k (dict: IDictionary<'k, 'v>) = dict.TryGetValue k |> Option.ofTry

/// Wrappers purely so one isn't passed in when the other is expected
module Time =
    /// When a replica recorded an event
    type Transaction<'t> = Transaction of 't
    /// When an event happened in the domain
    type Valid<'t> = Valid of 't

    [<Measure>] type ms
    let s = 1000L<ms>
    let m = 60L * s
    let h = 60L * m

[<Measure>] type events

module Event = 
    /// IDs must be globally unique and orderable. They should contain within
    /// them the physical valid time. This is so clients can generate their own
    /// IDs.
    /// TODO: make sure the physical time is not greater than current time.
    type ID = Ulid

    let newId (timestamp: int64<Time.ms>) (randomness: byte array) =
        Ulid(int64 timestamp, randomness)

[<IsReadOnly; Struct>]
type Address(id: byte array) =
    member _.Id = id
    override this.ToString() = 
        this.Id
        |> Array.map (fun b -> b.ToString("X2"))
        |> String.concat ""

// All of the messages must be idempotent
[<IsReadOnly; Struct>]
type Message<'e> =
    | Sync of Address
    | SendEvents of 
        since: uint64<events> * 
        destAddr: Address
    | StoreEvents of 
        from: (Address * uint64<events>) option *
        events:  (Event.ID * 'e) list

type Storage<'k, 'v>() =
    member val internal Events = SortedDictionary<'k, 'v>()
    member val internal AppendLog = ResizeArray<'k>()

    /// Returns events in transaction order, ie the order they were written
    member self.ReadEvents (since: uint64) =
        let events = 
            self.AppendLog.Skip (Checked.int since)
            |> Seq.choose (fun eventId -> 
                self.Events
                |> dictGet eventId
                |> Option.map (fun v -> (eventId, v))
            )

        let localLogicalTime = Checked.uint64 self.AppendLog.Count
        (events, localLogicalTime)

    member self.WriteEvents newEvents =
        for (k, v) in newEvents do
            if self.Events.TryAdd(k, v) then
                self.AppendLog.Add k

    override self.ToString() = 
        let es = 
            [for e in self.Events -> $"({e.Key}, {e.Value})" ]
            |> String.concat ";"

        let appendLogStr = 
            [for id in self.AppendLog -> $"{id}" ]
            |> String.concat ";"

        $"Storage [Events {es}] [AppendLog {appendLogStr}]"

type VersionVector() = 
    member val internal State = SortedDictionary<Address, uint64<events>>()

    member self.Update(addr: Address) =
        let c = self.State |> dictGet addr |> Option.defaultValue 0UL<events>
        self.State[addr] <- c + 1UL<events>

    member self.Get(addr: Address) =
        self.State |> dictGet addr |> Option.defaultValue 0UL<events>

    member self.Add(addr: Address, counter: uint64<events) =
        self.State[addr] <- counter

    override self.ToString() =
        [for v in self.State -> $"({v.Key}, {v.Value})" ] |> String.concat ";"

    
/// At this point we know nothing about the address, it's just an ID
type Database<'e>() =
    member val internal Storage = Storage<Event.ID, 'e>()
    member val internal VersionVector = VersionVector()

    /// Returns events in transaction order, ie the order they were written
    member self.ReadEvents (since: uint64<events>) =
        let (events, lc) = self.Storage.ReadEvents (uint64 since)
        let lc = Checked.uint64 lc
        (events, lc * 1uL<events>)

    member self.WriteEvents from newEvents =
        // If it came from another replica, update version vec to reflect this
        from |> Option.iter self.VersionVector.Add
        self.Storage.WriteEvents newEvents

    override self.ToString() = 
        $"Database [{self.Storage}] [VersionVector {self.Storage}]"

let converged (db1: Database<'e>) (db2: Database<'e>) =
    let (es1, es2) = (db1.Storage.Events, db2.Storage.Events)
    es1.SequenceEqual(es2)

type Replica<'e> = {Db: Database<'e>; Addr: Address}

type Sender<'e> = Address -> Message<'e> -> unit


/// Modifies the database based on msg, then returns response messages to send
let recv<'e> (src: Replica<'e>) (send: Sender<'e>) = function
    | Sync destAddr ->
        let since = src.Db.GetLogicalClock destAddr
        let (events, lc) = src.Db.ReadEvents since
        StoreEvents (Some (src.Addr, lc), List.ofSeq events) |> send destAddr
    | SendEvents (since, destAddr) ->
        let (events, lc) = src.Db.ReadEvents since
        StoreEvents (Some (src.Addr, lc), List.ofSeq events) |> send destAddr
    | StoreEvents (from, events) -> src.Db.WriteEvents from events