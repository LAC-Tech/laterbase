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

type Address<'e> =
    | InMemory of Database<'e>

// All of the messages must be idempotent
and Message<'e> =
    | Sync
    | SendEvents of Time.Transaction<Clock.Logical>
    | StoreEvents of 
        from: (Time.Transaction<Clock.Logical>) option *
        events:  (Event.ID * 'e) list

/// At this point we know nothing about the address, it's just an ID
and Database<'e>() =
    let appendLog = ResizeArray<Event.ID>()
    // These are both internal members so I can use them for equality
    member internal _.Events = SortedDictionary<Event.ID, 'e>()
    member internal _.VersionVector =
        SortedDictionary<Address<'e>, Time.Transaction<Clock.Logical>>()    

    member self.GetLogicalClock addr =
        self.VersionVector
        |> dictGet addr
        |> Option.defaultValue (Time.Transaction Clock.Logical.Epoch)

    /// Returns events in transaction order, ie the order they were written
    member self.ReadEvents (since: Time.Transaction<Clock.Logical>) =
        let (Time.Transaction since) = since
            
        let events = 
            appendLog.Skip (since.ToInt())
            |> Seq.choose (fun eventId -> 
                self.Events
                |> dictGet eventId
                |> Option.map (fun v -> (eventId, v))
            )
            |> Seq.toList

        let localLogicalTime = appendLog.Count |> Clock.Logical.FromInt
        (events, Time.Transaction localLogicalTime)

    member self.WriteEvents newEvents from =
        // If it came from another replica, update version vec to reflect this
        from |> Option.iter (fun (addr, lc) -> self.VersionVector[addr] <- lc)

        for (k, v) in newEvents do
            self.Events[k] <- v
            appendLog.Add k

    override self.ToString() = 
        let es = 
            [for e in self.Events -> $"({e.Key}, {e.Value})" ]
            |> String.concat ";"

        let appendLogStr = 
            [for id in appendLog -> $"{id}" ]
            |> String.concat ";"

        let vvStr =
            [for v in self.VersionVector -> $"({v.Key}, {v.Value})" ]
            |> String.concat ";"

        [
            "DATABASE"; 
            $"- Events = [{es}]";
            $"- Append Log = [{appendLogStr}]";
            $"- Version Vector = [{vvStr}]"
        ] |> String.concat "\n"
    
    override self.Equals(obj) =
        match obj with
        | :? Database<'e> as other ->
            self.Events = other.Events && 
            self.VersionVector = other.VersionVector
        | _ -> false

    override self.GetHashCode() =
        let hash = 17;
        let hash = hash * 23 * self.Events.GetHashCode()
        let hash = hash * 23 * self.VersionVector.GetHashCode()

        hash

    

/// src -> dest -> msg
type Sender<'e> = Address<'e> -> Address<'e> -> Message<'e> -> unit

let send srcAddr destAddr msg =
    match (srcAddr, destAddr) with
    | (InMemory localDb, InMemory remoteDb) ->
        match msg with 
        | Sync ->
            let since = localDb.GetLogicalClock destAddr
            let (events, lc) = localDb.ReadEvents since
            remoteDb.WriteEvents events (Some (srcAddr, lc))
        | SendEvents since ->
            let (events, lc) = localDb.ReadEvents since
            remoteDb.WriteEvents events (Some (srcAddr, lc))
        | StoreEvents (from, events) -> 
            from
            |> Option.map (fun lc -> (srcAddr, lc))
            |> remoteDb.WriteEvents events