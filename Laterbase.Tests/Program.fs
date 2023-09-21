open FsCheck
open Laterbase.Core
open System

Console.Clear ()

type EventVal = byte

let gen16Bytes = Arb.generate<byte> |> Gen.arrayOfLength 16

let genLogicalClock = Arb.generate<uint32<events>>

let genEventID =
    Gen.map2 
        (fun ts bytes -> Event.newId ts bytes)

        (Arb.generate<int64<Time.ms>> |> Gen.map abs)
        (Arb.generate<byte> |> Gen.arrayOfLength 10)

let genStorage<'a> = Arb.generate<unit> |> Gen.map (fun _ -> Storage<'a, 'a>())

let genDb<'id, 'e> = 
    Arb.generate<unit> |> Gen.map (fun _ -> Database<'id, 'e>())

let genAddr = gen16Bytes |> Gen.map Address

let genReplica<'id, 'e> = 
    Gen.map2
        (fun (db: Database<'id, 'e>) addr -> {Db = db; Addr = addr})
        genDb
        genAddr

type MyGenerators = 
    static member EventID() =
        {new Arbitrary<Event.ID>() with
            override _.Generator = genEventID                            
            override _.Shrinker _ = Seq.empty}

    static member Storage<'a>() =
        {new Arbitrary<Storage<'a, 'a>>() with
            override _.Generator = genStorage
            override _.Shrinker _ = Seq.empty}

    static member Database<'id, 'e>() =
        {new Arbitrary<Database<'id, 'e>>() with
            override _.Generator = genDb
            override _.Shrinker _ = Seq.empty}

    static member Address() =
        {new Arbitrary<Address>() with
            override _.Generator = genAddr
            override _.Shrinker _ = Seq.empty}

    static member Replica<'id, 'e>() =
        {new Arbitrary<Replica<'id, 'e>>() with
            override _.Generator = genReplica
            override _.Shrinker _ = Seq.empty}

let config = {
    Config.Quick with Arbitrary = [ typeof<MyGenerators> ]
}

let test descr testFn =
    printfn "# %A" descr
    Check.One(config, testFn)
    printfn "\n"

test 
    "ID's are unique"
    (fun (eventIds: Event.ID list) ->
        (eventIds |> List.distinct |> List.length) = (eventIds |> List.length))

test
    "Storing events locally is idempotent"
    (fun (inputEvents: (Event.ID * int64) list) ->
        let storage = Storage()
        seq {
            for _ in 1..100 do
                storage.WriteEvents inputEvents

                let (outputEvents, _) = storage.ReadEvents 0UL

                let outputEvents = outputEvents |> List.ofSeq

                // Storage will not store duplicates
                let inputEvents = 
                    inputEvents |> List.distinctBy (fun (k, _) -> k) 

                let result = inputEvents = outputEvents

                if (not result) then
                    eprintfn "ERROR: %A != %A" inputEvents outputEvents

                result
        }
        |> Seq.forall id
    )

let rec sendToNetwork network msgs = 
    for msg in msgs do
        let db = network |> Map.find msg.Dest
        let replica = {Db = db; Addr = msg.Dest}
        sendToNetwork network (send replica msg.Payload)

let acrossNetwork = 
    List.map (fun r -> r.Addr, r.Db) >> Map.ofList >> sendToNetwork

test 
    "two databases will have the same events if they sync with each other"
    (fun
        ((addr1, addr2) : (Address * Address))
        (events1 : (Event.ID * int) list)
        (events2 : (Event.ID * int) list) ->

        let r1 = {Db = Database(); Addr = addr1}
        let r2 = {Db = Database(); Addr = addr2}

        // Populate the two databases with separate events
        r1.Db.WriteEvents(None, events1)
        r2.Db.WriteEvents(None, events2)

        // Bi-directional sync
        send r1 (Sync r2.Addr) |> acrossNetwork [r1; r2]
        send r2 (Sync r1.Addr) |> acrossNetwork [r1; r2]

        converged r1.Db r2.Db
    )

(*
    Merging state-based CRDTs should be
    - commutative
    - idempotent
    - associative

    (Definition 2.3, Marc Shapiro, Nuno Preguiça, Carlos Baquero, and Marek Zawirski. Conflict-free replicated data types)
*)

test
    "syncing is commutative"
    (fun
        ((addrA1, addrB1, addrA2, addrB2) : 
            (Address * Address * Address * Address))
        (eventsA : (Event.ID * int) list)
        (eventsB : (Event.ID * int) list) ->
            
        let rA1 = {Db = Database(); Addr = addrA1}
        let rB1 = {Db = Database(); Addr = addrB1}

        let rA2 = {Db = Database(); Addr = addrA2}
        let rB2 = {Db = Database(); Addr = addrB2}

        // A replicas have same events
        rA1.Db.WriteEvents(None, eventsA)
        rA2.Db.WriteEvents(None, eventsA)

        // B replicas have same events
        rB1.Db.WriteEvents(None, eventsB)
        rB2.Db.WriteEvents(None, eventsB)

        // Sync 1 & 2 in different order; a . b = b . a
        send rB1 (Sync rA1.Addr) |> acrossNetwork [rA1; rB1]
        send rA2 (Sync rB2.Addr) |> acrossNetwork [rA2; rB2]

        converged rA1.Db rB2.Db
    )

test
    "syncing is idempotent"
    (fun
        (addr: Address)
        (events : (Event.ID * int) list) ->

        let replica = {Addr = addr; Db = Database()}
        let controlDb = Database()

        replica.Db.WriteEvents(None, events)
        controlDb.WriteEvents(None, events)

        send replica (Sync replica.Addr) |> acrossNetwork [replica]

        converged replica.Db controlDb
    )

test
    "syncing is associative"
    (fun
        ((addrA1, addrB1, addrC1, addrA2, addrB2, addrC2) : 
            (Address * Address * Address * Address * Address * Address))
        (eventsA : (Event.ID * int) list)
        (eventsB : (Event.ID * int) list)
        (eventsC : (Event.ID * int) list) ->

        let rA1 = {Db = Database(); Addr = addrA1}
        let rB1 = {Db = Database(); Addr = addrB1}
        let rC1 = {Db = Database(); Addr = addrC1}

        let rA2 = {Db = Database(); Addr = addrA2}
        let rB2 = {Db = Database(); Addr = addrB2}
        let rC2 = {Db = Database(); Addr = addrC2}

        // A replicas have same events
        rA1.Db.WriteEvents(None, eventsA)
        rA2.Db.WriteEvents(None, eventsA)

        // B replicas have same events
        rB1.Db.WriteEvents(None, eventsB)
        rB2.Db.WriteEvents(None, eventsB)

        // C replicas have same events
        rC1.Db.WriteEvents(None, eventsC)
        rC2.Db.WriteEvents(None, eventsC)

        let acrossNetwork1 = acrossNetwork [rA1; rB1; rC1]
        let acrossNetwork2 = acrossNetwork [rA2; rB2; rC2]

        // (a . b) . c
        send rB1 (Sync rA1.Addr) |> acrossNetwork1 
        send rA1 (Sync rC1.Addr) |> acrossNetwork1

        // a . (b . c)
        send rC2 (Sync rB2.Addr) |> acrossNetwork2
        send rB2 (Sync rA2.Addr) |> acrossNetwork2

        converged rC1.Db rA2.Db
        
    )