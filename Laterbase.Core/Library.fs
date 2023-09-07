module Laterbase.Core

open System
open System.Collections.Generic
open System.Linq
open System.Threading.Tasks

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

(** 
 * IDs must be globally unique and orderable. They should contain within 
 * them the physical valid time. This is so clients can generate their own
 * IDs.
 * 
 * TODO: make sure the physical time is not greater than current time.
*)

type EventID = struct
    val private Ulid: Ulid
    /// timestamp - milliseconds since epoch
    /// randonness - 10 random bytes
    new(timestamp: int64<Time.ms>, randomness: ReadOnlySpan<byte>) =
        { Ulid = Ulid(int64 timestamp, randomness) }
end

// Interface instead of a function so it can be compared
type IAddress<'e> =
    abstract Send: msg: Message<'e> -> Result<unit, Task<string>>
// All of the messages must be idempotent
and Message<'e> =
    | SyncWith of IAddress<'e>
    | SendEvents of EventRequest<'e> 
    | StoreEvents of EventResponse<'e>
and EventResponse<'e> = { 
    From: (IAddress<'e> * Time.Transaction<Clock.Logical>) option
    Events: (EventID * 'e) list
}
and EventRequest<'e> = {
    Since : Time.Transaction<Clock.Logical>
    ToAddr: IAddress<'e> 
}

/// At this point we know nothing about the address, it's just an ID
type Database<'e, 'addr>(addr: 'addr) =
    let events = SortedDictionary<EventID, 'e>()
    let appendLog = ResizeArray<EventID>()
    let versionVector = SortedDictionary<'addr, Clock.Logical>()
    
    let event_matching_id eventId =
        events |> dictGet eventId |> Option.map (fun v -> (eventId, v))

    member _.GetLogicalClock addr =
        versionVector
        |> dictGet addr
        |> Option.defaultValue Clock.Logical.Epoch

    member _.SendEvents (since: Time.Transaction<Clock.Logical>) =
        let (Time.Transaction since) = since
            
        let events = 
            appendLog.Skip (since.ToInt())
            |> Seq.choose event_matching_id
            |> Seq.toList

        let localLogicalTime = appendLog.Count |> Clock.Logical.FromInt
        (events, Time.Transaction localLogicalTime)

    member _.StoreEvents from newEvents =
        let updateClock (addr, Time.Transaction lc) = versionVector[addr] <- lc
        // If it came from another replica, update version vec to reflect this
        from |> Option.iter updateClock

        for (k, v) in newEvents do
            events[k] <- v
            appendLog.Add k

/// A replica is a database backed replica of the events, as well as an Actor
type Replica<'e>(addr: IAddress<'e>) =
    let db = Database(addr)
    member _.Address = addr

    member this.Send<'e> msg =
        match msg with
        | SyncWith addr ->
            let outgoingMsg = SendEvents { 
                Since = Time.Transaction (db.GetLogicalClock addr) 
                ToAddr = this.Address
            }
            addr.Send outgoingMsg
        | SendEvents {Since = since; ToAddr = toAddr} ->
            let (events, t) = db.SendEvents since
            let outgoingMsg = StoreEvents {
                From = Some (this.Address, t)
                Events = events
            }
            toAddr.Send outgoingMsg
        | StoreEvents {From = from; Events = events } ->
            db.StoreEvents from events
            Ok ()
