﻿open FsCheck
open Laterbase
open Laterbase.Core
open System

Console.Clear ()

type EventVal = byte

let gen16Bytes = Arb.generate<byte> |> Gen.arrayOfLength 16

let genLogicalClock = Arb.generate<uint32<events>>

let genEventID =
    Gen.map2 
        (fun ts bytes -> Event.newId ts bytes)
        (Arb.generate<int64<valid ms>> |> Gen.map abs)
        (Arb.generate<byte> |> Gen.arrayOfLength 10)

let genAddr = gen16Bytes |> Gen.map Address

type MyGenerators = 
    static member EventID() =
        {new Arbitrary<Event.ID>() with
            override _.Generator = genEventID                            
            override _.Shrinker _ = Seq.empty}

    static member Address() =
        {new Arbitrary<Address>() with
            override _.Generator = genAddr
            override _.Shrinker _ = Seq.empty}

let config = {
    Config.Quick with Arbitrary = [ typeof<MyGenerators> ]
}

let test descr testFn =
    printfn "# %A" descr
    Check.One(config, testFn)
    printfn "\n"

test
    "Can read back the events you store"
    (fun (inputEvents: (Event.ID * int64) list) (addr: Address) ->
        let r: IReplica<int64> = LocalReplica(addr, fun _ _ -> ())
        seq {
            for _ in 1..100 do
                r.Recv (StoreNew inputEvents)

                let outputEvents = r.Read({ByTime = LogicalTxn; Limit = 0uy})

                let outputEvents = 
                    outputEvents 
                    |> Seq.map (fun (k, v) -> (k, v.Payload))
                    |> Seq.toList

                // Storage will not store duplicates
                let inputEvents = 
                    inputEvents |> List.distinctBy (fun (k, _) -> k) 

                inputEvents = outputEvents
        }
        |> Seq.forall id
    )

let oneTestReplica addr = Simulated.Replicas[|addr|].[0]

let twoTestReplicas (addr1, addr2) =
    let rs = Simulated.Replicas [|addr1; addr2|]
    (rs[0], rs[1])

let threeTestReplicas (addr1, addr2, addr3) =
    let rs = Simulated.Replicas [|addr1; addr2; addr3|]
    (rs[0], rs[1], rs[2])

let replicasConverged (r1: IReplica<'e>) (r2: IReplica<'e>) =
    let query = {ByTime = PhysicalValid; Limit = 0uy}
    let es1 = r1.Read(query) |> Seq.toList
    printfn $"es1 = {es1}"
    let es1 = r1.Read(query) |> Seq.map (fun (k, v) -> (k, v.Payload))

    let es2 = r1.Read(query) |> Seq.toList
    printfn $"es2 = {es2}"
    let es2 = r2.Read(query) |> Seq.map (fun (k, v) -> (k, v.Payload))

    Seq.equal es1 es2

test 
    "two databases will have the same events if they sync with each other"
    (fun
        ((addr1, adrr2) : (Address * Address))
        (events1 : (Event.ID * int) list)
        (events2 : (Event.ID * int) list) ->
    
        let (r1, r2) = twoTestReplicas(addr1, adrr2)

        // Populate the two databases with separate events
        r1.Recv(StoreNew events1)
        r2.Recv(StoreNew events2)

        // Bi-directional sync
        r1.Recv(Sync r2.Addr)
        r2.Recv(Sync r1.Addr)

        replicasConverged r1 r2        
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

        let (rA1, rB1) = twoTestReplicas(addrA1, addrB1)
        let (rA2, rB2) = twoTestReplicas(addrA2, addrB2)

        // A replicas have same events
        rA1.Recv(StoreNew eventsA)
        rA2.Recv(StoreNew eventsA)

        // B replicas have same events
        rB1.Recv(StoreNew eventsB)
        rB2.Recv(StoreNew eventsB)

        // Sync 1 & 2 in different order; a . b = b . a
        rB1.Recv(Sync rA1.Addr)
        rA2.Recv(Sync rB2.Addr)

        replicasConverged rA1 rB2
    )

test
    "syncing is idempotent"
    (fun
        (addr: Address)
        (controlAddr: Address)
        (events : (Event.ID * Event.Val<int>) list) ->

        let (replica, controlReplica) = twoTestReplicas(addr, controlAddr)

        replica.Recv(StoreNew events)
        controlReplica.Recv(StoreNew events)

        replica.Recv (Sync replica.Addr)

        replicasConverged replica controlReplica
    )

test
    "syncing is associative"
    (fun
        ((addrA1, addrB1, addrC1, addrA2, addrB2, addrC2) : 
            (Address * Address * Address * Address * Address * Address))
        (eventsA : (Event.ID * int) list)
        (eventsB : (Event.ID * int) list)
        (eventsC : (Event.ID * int) list) ->

        let (rA1, rB1, rC1) = threeTestReplicas(addrA1, addrB1, addrC1)
        let (rA2, rB2, rC2) = threeTestReplicas(addrA2, addrB2, addrC2)

        // A replicas have same events
        rA1.Recv (StoreNew eventsA)
        rA2.Recv (StoreNew eventsA)

        // B replicas have same events
        rB1.Recv (StoreNew eventsB)
        rB2.Recv (StoreNew eventsB)

        // C replicas have same events
        rC1.Recv (StoreNew eventsC)
        rC2.Recv (StoreNew eventsC)

        // (a . b) . c
        rB1.Recv (Sync rA1.Addr)
        rA1.Recv (Sync rC1.Addr)

        // a . (b . c)
        rC2.Recv (Sync rB2.Addr)
        rB2.Recv (Sync rA2.Addr)

        replicasConverged rC1 rA2
    )
