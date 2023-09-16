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
    type ID =
        struct
            val ulid: Ulid
            new (timestamp: int64<Time.ms>, randomness: byte array) =
                let ulid = Ulid(int64 timestamp, randomness)
                { ulid = ulid }
            override self.ToString() = self.ulid.ToString()
        end

[<IsReadOnly; Struct>]
type Address(id: byte array) =
    member _.Id = id
    override this.ToString() = 
        this.Id
        |> Array.map (fun b -> b.ToString("X2"))
        |> String.concat ""

// All of the messages must be idempotent
type Message<'e> =
    | Sync of Address
    | SendEvents of 
        since: uint32<events> * 
        destAddr: Address
    | StoreEvents of 
        from: (Address * uint32<events>) option *
        events:  (Event.ID * 'e) list

type Storage<'k, 'v>() =
    member val internal Events = SortedDictionary<'k, 'v>()
    member val internal AppendLog = ResizeArray<'k>()

    /// Returns events in transaction order, ie the order they were written
    member self.ReadEvents (since: int) =
        let events = 
            self.AppendLog.Skip since
            |> Seq.choose (fun eventId -> 
                self.Events
                |> dictGet eventId
                |> Option.map (fun v -> (eventId, v))
            )
            |> Seq.toList

        let localLogicalTime = self.AppendLog.Count
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

    override self.Equals(obj) =
        match obj with
        | :? Storage<'k, 'v> as other -> self.Events = other.Events
        | _ -> false

/// At this point we know nothing about the address, it's just an ID
type Database<'e>() =
    member val internal Storage = Storage<Event.ID, 'e>()
    member val internal VersionVector =
        SortedDictionary<Address, uint32<events>>()    

    member self.GetLogicalClock addr =
        self.VersionVector
        |> dictGet addr
        |> Option.defaultValue 0u<events>

    /// Returns events in transaction order, ie the order they were written
    member self.ReadEvents (since: uint32<events>) =
        let since = Checked.int since
        let (events, lc) = self.Storage.ReadEvents since
        let lc = Checked.uint32 lc
        (events, lc * 1u<events>)

    member self.WriteEvents from newEvents =
        // If it came from another replica, update version vec to reflect this
        from |> Option.iter (fun (addr, lc) -> self.VersionVector[addr] <- lc)
        self.Storage.WriteEvents newEvents

    override self.ToString() = 
        let vvStr =
            [for v in self.VersionVector -> $"({v.Key}, {v.Value})" ]
            |> String.concat ";"

        $"Database [{self.Storage}] [VersionVector {vvStr}]"

type Replica<'e> = {Db: Database<'e>; Addr: Address}

type Sender<'e> = Address -> Message<'e> -> unit

/// Modifies the database based on msg, then returns response messages to send
let recv<'e> 
    (src: Replica<'e>)
    (send: Sender<'e>)
    (msg: Message<'e>) =
    match msg with 
    | Sync destAddr ->
        let since = src.Db.GetLogicalClock destAddr
        let (events, lc) = src.Db.ReadEvents since
        StoreEvents (Some (src.Addr, lc), events) |> send destAddr
    | SendEvents (since, destAddr) ->
        let (events, lc) = src.Db.ReadEvents since
        StoreEvents (Some (src.Addr, lc), events) |> send destAddr
    | StoreEvents (from, events) -> src.Db.WriteEvents from events

