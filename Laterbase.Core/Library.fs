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
let dictGetOrDefault k defaultValue (dict: IDictionary<'k, 'v>)  = 
    dictGet k dict |> Option.defaultValue defaultValue

/// Wrappers purely so one isn't passed in when the other is expected
module Time =
    /// When a replica recorded an event
    type Transaction<'t> = Transaction of 't
    /// When an event happened in the domain
    type Valid<'t> = Valid of 't

    [<Measure>] type ms
    [<Measure>] type s
    [<Measure>] type m
    [<Measure>] type h
    let msPerS: int64<ms/s> = 1000L<ms/s>
    let sPerM: int64<s/m> = 60L<s/m>
    let mPerH: int64<m/h> = 60L<m/h>

[<Measure>] type events
[<Measure>] type received
[<Measure>] type sent

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

[<Struct; IsReadOnly>]
type MessagePayload<'k, 'v> =
    | Sync of Address
    | StoreEvents of 
        from: (Address * uint64<received events>) option *
        events:  ('k * 'v) list
    //| StoreEventsAck of uint64<sent events>

// All of the messages must be idempotent
[<Struct; IsReadOnly>] 
type Message<'k, 'v> = {
    Dest: Address;
    Payload: MessagePayload<'k, 'v>
}

type Storage<'k, 'v>() =
    member val internal Events = SortedDictionary<'k, 'v>()
    member val internal AppendLog = ResizeArray<'k>()

    /// Returns events in transaction order, ie the order they were written
    member self.ReadEvents(since: uint64) =
        let kvPair eventId =
            dictGet eventId self.Events  
            |> Option.map (fun v -> (eventId, v))

        let events =
            self.AppendLog.Skip (Checked.int since - 1)
            |> Seq.choose kvPair
        
        let totalNumEvents = Checked.uint64 self.AppendLog.Count
        (events, totalNumEvents)

    member self.WriteEvents newEvents =
        for (k, v) in newEvents do
            if self.Events.TryAdd(k, v) then
                self.AppendLog.Add k

    override self.ToString() =
        let es = 
            [for e in self.Events -> $"{e.Key}, {e.Value}" ]
            |> String.concat "; "

        let appendLogStr =
            [for id in self.AppendLog -> $"{id}" ]
            |> String.concat "; "

        $"└ events = [{es}]\n└ log = [{appendLogStr}]"

type LogicalClock() =
    // Double sided counter so we can just send single counters across the network
    // TODO save some space and just store the difference?
    member val internal Sent = 
        SortedDictionary<Address, uint64<sent events>>()
    member val internal Received = 
        SortedDictionary<Address, uint64<received events>>()  

    member self.EventsReceivedFrom addr = 
        dictGetOrDefault addr 0UL<received events> self.Received
    
    member self.EventsSentFrom addr = 
        dictGetOrDefault addr 0UL<sent events> self.Sent

    member self.AddReceived(addr: Address, counter: uint64<received events>) =
        self.Received[addr] <- counter

    member self.AddSent(addr: Address, counter: uint64<sent events>) =
        self.Sent[addr] <- counter

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
    
/// At this point we know nothing about the address, it's just an ID
type Database<'id, 'e>() =
    member val internal Storage = Storage<'id, 'e>()
    member val internal LogicalClock = LogicalClock()

    /// Returns new events from the perspective of the destAddr
    member self.ReadEvents (destAddr: Address) =
        let since = self.LogicalClock.EventsSentFrom destAddr
        let (events, totalNumEvents) = self.Storage.ReadEvents (uint64 since)
        let totalNumEvents = totalNumEvents * 1uL<received events>
        (events, totalNumEvents)

    member self.WriteEvents(from, newEvents) =
        // If it came from another replica, update version vec to reflect this
        from |> Option.iter self.LogicalClock.AddReceived
        self.Storage.WriteEvents newEvents

    override self.ToString() = 
        $"Database\n{self.Storage}\n{self.LogicalClock}"

let converged (db1: Database<'id, 'e>) (db2: Database<'id, 'e>) =
    let (es1, es2) = (db1.Storage.Events, db2.Storage.Events)
    es1.SequenceEqual(es2)

type Replica<'id, 'e> = {Addr: Address; Db: Database<'id, 'e>}

type Sender<'id, 'e> = Address -> Message<'id, 'e> -> unit

/// Modifies the database based on msg, then returns response messages to send
let send<'id, 'e> (src: Replica<'id, 'e>) = function
    | Sync destAddr ->
        let (events, lc) = src.Db.ReadEvents destAddr
        let payload = StoreEvents (Some (src.Addr, lc), List.ofSeq events)
        seq { {Dest = destAddr; Payload = payload } }
    | StoreEvents (from, events) -> 
        src.Db.WriteEvents(from, events)
        Seq.empty
