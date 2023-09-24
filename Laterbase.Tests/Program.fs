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
                r.Send (Store (inputEvents, None))

                let outputEvents = 
                    r.Read({ByTime = LogicalTxn; Limit = 0uy})

                let outputEvents = outputEvents |> List.ofSeq


                // Storage will not store duplicates
                let inputEvents = 
                    inputEvents |> List.distinctBy (fun (k, _) -> k) 

                inputEvents = outputEvents
        }
        |> Seq.forall id
    )

let testReplicas<'e> addrs =
    let network = ResizeArray<IReplica<'e>>()
    let sendMsg addr = network.Find(fun r -> r.Addr = addr).Send
    let addrToReplica addr = LocalReplica(addr, sendMsg) :> IReplica<'e>
    let rs = List.map addrToReplica addrs
    network.AddRange(rs)
    rs

let oneTestReplica addr = testReplicas [addr] |> List.head

let twoTestReplicas (addr1, addr2) =
    match testReplicas [addr1; addr2] with
    | [r1; r2] -> (r1, r2)
    | _ -> failwith "expecting two replicas"

let threeTestReplicas (addr1, addr2, addr3) =
    match testReplicas [addr1; addr2; addr3] with
    | [r1; r2; r3] -> (r1, r2, r3)
    | _ -> failwith "expecting three replicas"

let replicasConverged (r1: IReplica<'e>) (r2: IReplica<'e>) =
    let query = {ByTime = PhysicalValid; Limit = 0uy}
    let es1 = r1.Read(query)
    let es2 = r2.Read(query)
    Seq.equal es1 es2

test 
    "two databases will have the same events if they sync with each other"
    (fun
        ((addr1, addr2) : (Address * Address))
        (events1 : (Event.ID * int) list)
        (events2 : (Event.ID * int) list) ->
    
        let (r1, r2) = twoTestReplicas(addr1, addr2)

        // Populate the two databases with separate events
        r1.Send (Store (events1, None))
        r2.Send (Store (events2, None))

        // Bi-directional sync
        r1.Send (Sync r2.Addr)
        r2.Send (Sync r1.Addr)

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
        rA1.Send(Store(eventsA, None))
        rA2.Send(Store(eventsA, None))

        // B replicas have same events
        rB1.Send(Store(eventsB, None))
        rB2.Send(Store(eventsB, None))

        // Sync 1 & 2 in different order; a . b = b . a
        rB1.Send(Sync rA1.Addr)
        rA2.Send(Sync rB2.Addr)

        replicasConverged rA1 rB2
    )

test
    "syncing is idempotent"
    (fun
        (addr: Address)
        (controlAddr: Address)
        (events : (Event.ID * int) list) ->

        let (replica, controlReplica) = twoTestReplicas(addr, controlAddr)

        replica.Send (Store(events, None))
        controlReplica.Send(Store(events, None))

        replica.Send (Sync replica.Addr)

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
        rA1.Send (Store(eventsA, None))
        rA2.Send (Store(eventsA, None))

        // B replicas have same events
        rB1.Send (Store(eventsB, None))
        rB2.Send (Store(eventsB, None))

        // C replicas have same events
        rC1.Send (Store(eventsC, None))
        rC2.Send (Store(eventsC, None))

        // (a . b) . c
        rB1.Send (Sync rA1.Addr)
        rA1.Send (Sync rC1.Addr)

        // a . (b . c)
        rC2.Send (Sync rB2.Addr) 
        rB2.Send (Sync rA2.Addr)

        replicasConverged rC1 rA2
    )
