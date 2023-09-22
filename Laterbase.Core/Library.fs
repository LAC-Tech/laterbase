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

type ConstraintViolation<'a> (reason: string, thing: 'a) =
    inherit System.Exception (reason) 

/// When a replica recorded an event
[<Measure>] type transaction

/// When an event happened in the domain
[<Measure>] type valid

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
    /// Valid time is used so:
    /// - replicas can create their own IDs
    /// - we can backdate
    /// TODO: make sure the timestamp is not greater than current time.
    type ID = Ulid

    let newId (timestamp: int64<valid ms>) (randomness: byte array) =
        Ulid(int64 timestamp, randomness)

    type Stream<'e> = (ID * 'e) seq

[<IsReadOnly; Struct>]
type Address(id: byte array) =
    member _.Id = id
    // Hex string for compactness
    override this.ToString() = 
        this.Id
        |> Array.map (fun b -> b.ToString("X2"))
        |> String.concat ""

[<Struct; IsReadOnly>]
type MessagePayload<'e> =
    | Sync of Address
    | StoreEvents of 
        from: (Address * uint64<received events>) option *
        events:  (Event.ID * 'e) list
    //| StoreEventsAck of uint64<sent events>

// All of the messages must be idempotent
[<Struct; IsReadOnly>] 
type Message<'e> = {
    Dest: Address;
    Payload: MessagePayload<'e>
}

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

type Storage<'k, 'v>() =
    member val internal Events = SortedDictionary<'k, 'v>()
    (**
        This imposes a total order on a partial order.
        IE events written to at the same time are concurrent.
        Should it be a jagged array with concurrent events stored together? 
    *)
    member val internal AppendLog = ResizeArray<'k>()

    /// Returns events in transaction order, ie the order they were written
    member self.ReadEvents(since: uint64) =
        let kvPair eventId =
            dictGet eventId self.Events  
            |> Option.map (fun v -> (eventId, v))

        let events =
            Seq.skip (Checked.int since - 1) self.AppendLog
            |> Seq.choose kvPair
        
        let totalNumEvents = Checked.uint64 self.AppendLog.Count
        (events, totalNumEvents)

    member self.WriteEvents newEvents =
        for (k, v) in newEvents do
            if self.Events.TryAdd(k, v) then
                self.AppendLog.Add k

    // Not sure if 'consistent' is the word here
    member self.Consistent() =
        self.Events.Count = self.AppendLog.Count

    override self.ToString() =
        let es = 
            [for e in self.Events -> $"{e.Key}, {e.Value}" ]
            |> String.concat "; "

        let appendLogStr =
            [for id in self.AppendLog -> $"{id}" ]
            |> String.concat "; "

        $"└ events = [{es}]\n└ log = [{appendLogStr}]"

type DatabaseViewData<'e> = {
    Events: Event.Stream<'e>
    AppendLog: Event.ID seq
    LogicalClock: (Address * uint64<events sent> * uint64<events received>) seq
}

type IDatabase<'e> =
    /// Returns new events from the perspective of the destAddr
    abstract member ReadEvents: 
        destAddr: Address -> Event.Stream<'e> * uint64<events received>

    abstract member WriteEvents:
        from: (Address * uint64<events received>) option * newEvents: Event.Stream<'e> ->
        unit

type LocalDatabase<'e>() =
    member val private Storage = Storage<Event.ID, 'e>()
    member val private LogicalClock = LogicalClock()

    interface IDatabase<'e> with    
        member self.ReadEvents (destAddr: Address) =
            let since = self.LogicalClock.EventsSentFrom destAddr
            let (events, totalNumEvents) = self.Storage.ReadEvents (uint64 since)
            let totalNumEvents = totalNumEvents * 1uL<received events>
            (events, totalNumEvents)

        member self.WriteEvents(from, newEvents) =
            // If from another replica, update logical clock to reflect this
            from |> Option.iter self.LogicalClock.AddReceived
            self.Storage.WriteEvents newEvents

            if not (self.Storage.Consistent()) then
                raise (ConstraintViolation ("Storage is inconsistent", self))

    member self.View() =
        {
            Events = 
                self.Storage.Events  |>Seq.map (fun kvp -> kvp.Key, kvp.Value)
            AppendLog = self.Storage.AppendLog
            LogicalClock = self.LogicalClock.View()
        }

    override self.ToString() = 
        $"Database\n{self.Storage}\n{self.LogicalClock}"

/// TODO: this belongs in test
let converged<'e> (db1: LocalDatabase<'e>) (db2: LocalDatabase<'e>) =
    let (es1, es2) = (db1.View().Events, db2.View().Events)
    es1.SequenceEqual(es2)

type Replica<'e, 'db when 'db :> IDatabase<'e>> = { Addr: Address; Db: 'db }

type Sender<'e> = Address -> Message<'e> -> unit

/// Modifies the database based on msg, then returns response messages to send
let send<'e> 
    (sendAcrossNetwork: Message<'e> -> unit) 
    (src: Replica<'e, IDatabase<'e>>) = function
    | Sync destAddr ->
        let (events, lc) = src.Db.ReadEvents destAddr
        let payload = StoreEvents (Some (src.Addr, lc), List.ofSeq events)
        sendAcrossNetwork {Dest = destAddr; Payload = payload }
    | StoreEvents (from, events) -> src.Db.WriteEvents(from, events)

