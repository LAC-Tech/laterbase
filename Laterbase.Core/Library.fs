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
    type Logical = struct
        val private N: uint64
        private new(n: uint64) = { N = n }
        member this.ToInt () = Checked.int this.N
        static member FromInt n = Logical (Checked.uint64 n)
        static member Epoch = Logical 0UL
    end

module Event =
    (**
	 * IDs must be globally unique and orderable. They should contain within 
	 * them the physical valid time. This is so clients can generate their own
	 * IDs.
	 * 
	 * TODO: make sure the physical time is not greater than current time.
     * TODO: pass in own seed and timestamp
	 *)
    type ID = struct
        val private Ulid: Ulid
        /// timestamp - milliseconds since epoch
        /// randonness - 10 random bytes
        new(timestamp: int64<Time.ms>, randomness: ReadOnlySpan<byte>) =
            { Ulid = Ulid(int64 timestamp, randomness) }
    end

// Interface instead of a function so it can be compared
type IAddress<'e> =
    abstract Send: msg: Message<'e> -> Result<unit, Threading.Tasks.Task<string>>
// All of the messages must be idempotent
and Message<'e> =
    | SyncWith of IAddress<'e>
    | SendEvents of EventRequest<'e> 
    | StoreEvents of EventResponse<'e>
and EventResponse<'e> = { 
    From: (IAddress<'e> * Time.Transaction<Clock.Logical>) option
    Events: (Event.ID * 'e) list
}
and EventRequest<'e> = {
    Since : Time.Transaction<Clock.Logical>
    ToAddr: IAddress<'e> 
}

/// A replica is a database backed replica of the events, as well as an Actor
type Replica<'e>(addr: IAddress<'e>) =
    let events = SortedDictionary<Event.ID, 'e>()
    let appendLog = ResizeArray<Event.ID>()
    let versionVector = SortedDictionary<IAddress<'e>, Clock.Logical>()
    
    let event_matching_id eventId = 
        events |> dictGet eventId |> Option.map (fun v -> (eventId, v))

    member _.Address = addr

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

        {
            From = Some (addr, Time.Transaction localLogicalTime);
            Events = events
        }

    member _.StoreEvents {From = from; Events = es} =
        let updateClock (addr, Time.Transaction lc) = versionVector[addr] <- lc
        // If it came from another replica, update version vec to reflect this
        from |> Option.iter updateClock

        for (k, v) in es do
            events[k] <- v
            appendLog.Add(k)

let send<'e> msg (replica: Replica<'e>) =
    match msg with
    | SyncWith addr ->
        let outgoingMsg = SendEvents { 
            Since = Time.Transaction (replica.GetLogicalClock addr) 
            ToAddr = replica.Address
        }
        addr.Send outgoingMsg
    | SendEvents {Since = since; ToAddr = toAddr} ->
        let outgoingMsg = StoreEvents (replica.SendEvents since)
        toAddr.Send outgoingMsg
    | StoreEvents eventRes -> 
        replica.StoreEvents eventRes
        Ok ()
