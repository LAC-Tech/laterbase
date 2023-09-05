module Library
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

module Clock =
    type Physical = DateTimeOffset

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
        new(timestamp: int64, randomness: ReadOnlySpan<byte>) =
            { Ulid = Ulid(timestamp, randomness) }
    end

// Interface instead of a function so it can be compared
type IAddress<'e> =
    abstract Send: msg: Message<'e> -> unit
// All of the messages must be idempotent
and Message<'e> =
    | SyncWith of IAddress<'e>
    | SendEvents of
        since : Time.Transaction<Clock.Logical> * 
        toAddr: IAddress<'e> 
    | StoreEvents of
        from: (IAddress<'e> * Time.Transaction<Clock.Logical>) option *
        events: (Event.ID * 'e) list

/// A replica is a database backed replica of the events, as well as an Actor
type Replica<'e>(addr: IAddress<'e>) =
    let events = SortedDictionary<Event.ID, 'e>()
    let appendLog = ResizeArray<Event.ID>()
    let versionVector = SortedDictionary<IAddress<'e>, Clock.Logical>()
    
    let event_matching_id eventId = 
        events |> dictGet eventId |> Option.map (fun v -> (eventId, v))

    let store_events es = 
        for (k, v) in es do
            events[k] <- v
            appendLog.Add(k)

    member _.Send msg =
        match msg with
        | SyncWith addr ->
            let lc = 
                versionVector 
                |> dictGet addr 
                |> Option.defaultValue Clock.Logical.Epoch

            let msg = SendEvents(since = Time.Transaction lc, toAddr = addr)
            addr.Send(msg)
        | SendEvents (since, toAddr) ->
            let (Time.Transaction since) = since 
            
            let events = 
                appendLog.Skip (since.ToInt())
                |> Seq.choose event_matching_id
                |> Seq.toList

            let local_logical_time = appendLog.Count |> Clock.Logical.FromInt

            let msg = StoreEvents(
                from = Some (addr, Time.Transaction (local_logical_time)),
                events = events
            )
            
            toAddr.Send msg
        | StoreEvents (from, events) ->
            from |> Option.iter (fun (addr, Time.Transaction lc) ->
                versionVector[addr] <- lc
            )

            store_events events