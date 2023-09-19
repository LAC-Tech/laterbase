﻿open FsCheck
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
        sendToNetwork network (recv replica msg.Payload)

test 
    "two databases will have the same events if they sync with each other"
    (fun 
        ((addr1, events1) : (Address * (Event.ID * bool) list))
        ((addr2, events2) : (Address * (Event.ID * bool) list)) ->

        let r1 = {Db = Database(); Addr = addr1}
        let r2 = {Db = Database(); Addr = addr2}

        // Simulating a network
        let network = Map.ofList [r1.Addr, r1.Db; r2.Addr, r2.Db]
        let send = sendToNetwork network

        // Populate the two databases with separate events
        r1.Db.WriteEvents None events1
        r2.Db.WriteEvents None events2

        recv r1 (Sync r2.Addr) |> send
        recv r2 (Sync r1.Addr) |> send

        converged r1.Db r2.Db
    )
