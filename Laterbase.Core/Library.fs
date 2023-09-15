module Laterbase.Core

open System
open System.Collections.Generic
open System.Linq

open NetUlid

(* Convenience functions *)
module Option =
    let ofTry (found, value) = if found then Some value else None

let dictGet k (dict: IDictionary<'k, 'v>) = dict.TryGetValue k |> Option.ofTry

/// Wrappers purely so one isn't passed in when the other is expected
/// TODO: is this a usecase for measurements?
module Time =
    /// When a replica recorded an event
    type Transaction<'t> = Transaction of 't
    /// When an event happened in the domain
    type Valid<'t> = Valid of 't

    [<Measure>] type ms
    let s = 1000L<ms>
    let m = 60L * s
    let h = 60L * m

module Clock =
    type Logical(n: uint64) = struct
        member _.ToInt () = Checked.int n
        static member FromInt n = Logical (Checked.uint64 n)
        static member Epoch = Logical 0UL
    end

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

type Address = {id: byte array}

// All of the messages must be idempotent
type Message<'e> =
    | Sync of Address
    | SendEvents of 
        since: Time.Transaction<Clock.Logical> * 
        destAddr: Address
    | StoreEvents of 
        from: (Address * Time.Transaction<Clock.Logical>) option *
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
            else
                eprintf "event %A already exists" k

    override self.ToString() = 
        let es = 
            [for e in self.Events -> $"({e.Key}, {e.Value})" ]
            |> String.concat ";"

        let appendLogStr = 
            [for id in self.AppendLog -> $"{id}" ]
            |> String.concat ";"

        [
            "STORAGE"; 
            $"- Events = [{es}]";
            $"- Append Log = [{appendLogStr}]";
        ] |> String.concat "\n"

    override self.Equals(obj) =
        match obj with
        | :? Storage<'k, 'v> as other -> self.Events = other.Events
        | _ -> false

/// At this point we know nothing about the address, it's just an ID
type Database<'e>() =
    member val internal Storage = Storage<Event.ID, 'e>()
    member val internal VersionVector =
        SortedDictionary<Address, Time.Transaction<Clock.Logical>>()    

    member self.GetLogicalClock addr =
        self.VersionVector
        |> dictGet addr
        |> Option.defaultValue (Time.Transaction Clock.Logical.Epoch)

    /// Returns events in transaction order, ie the order they were written
    member self.ReadEvents (since: Time.Transaction<Clock.Logical>) =
        let (Time.Transaction since) = since
        let (events, lc) = self.Storage.ReadEvents (since.ToInt())
        (events, lc |> Clock.Logical.FromInt |> Time.Transaction)

    member self.WriteEvents newEvents from =
        // If it came from another replica, update version vec to reflect this
        from |> Option.iter (fun (addr, lc) -> self.VersionVector[addr] <- lc)
        self.Storage.WriteEvents newEvents

    override self.ToString() = 
        let vvStr =
            [for v in self.VersionVector -> $"({v.Key}, {v.Value})" ]
            |> String.concat ";"

        [
            "DATABASE"; 
            $"- Storage = [{self.Storage}]";
            $"- Version Vector = [{vvStr}]"
        ] |> String.concat "\n"

/// Modifies the database based on msg, then returns response messages to send
let send<'e> (srcDb: Database<'e>) (srcAddr: Address) (msg: Message<'e>) =
    match msg with 
    | Sync destAddr ->
        let since = srcDb.GetLogicalClock destAddr
        let (events, lc) = srcDb.ReadEvents since
        seq { (destAddr, StoreEvents (Some (srcAddr, lc), events)) }
    | SendEvents (since, destAddr) ->
        let (events, lc) = srcDb.ReadEvents since
        seq { (destAddr, StoreEvents (Some (srcAddr, lc), events)) }
    | StoreEvents (from, events) -> 
       srcDb.WriteEvents events from
       Seq.empty
