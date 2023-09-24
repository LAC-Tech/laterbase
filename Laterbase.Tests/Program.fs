open FsCheck
open Laterbase.Core
open System
open System.Linq

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
                r.Send (StoreEvents (None, inputEvents))

                let outputEvents = 
                    r.Read({ByTime = Sort.Time.LogicalTxn; Limit = 0uy})

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

let sendToReplicas<'e> (network: ResizeArray<IReplica<'e>>) addr =
    let r = network.Find(fun r -> r.Address = addr)
    r.Send

test 
    "two databases will have the same events if they sync with each other"
    (fun
        ((addr1, addr2) : (Address * Address))
        (events1 : (Event.ID * int) list)
        (events2 : (Event.ID * int) list) ->
    
        let network = ResizeArray<IReplica<int>>()
        let sendMsg = sendToReplicas network 
        let r1: IReplica<int> = LocalReplica(addr1, sendMsg)
        let r2: IReplica<int> = LocalReplica(addr2, sendMsg)

        network.AddRange([r1; r2])

        // Populate the two databases with separate events
        r1.Send (StoreEvents (None, events1))
        r2.Send (StoreEvents (None, events2))

        // Bi-directional sync
        r1.Send (Sync r2.Address)
        r2.Send (Sync r1.Address)

        let query = {ByTime = Sort.Time.PhysicalValid; Limit = 0uy}
        let es1 = r1.Read(query) |> Seq.toList
        let es2 = r2.Read(query) |> Seq.toList

        Seq.equal es1 es2
    )

(*
    Merging state-based CRDTs should be
    - commutative
    - idempotent
    - associative

    (Definition 2.3, Marc Shapiro, Nuno Preguiça, Carlos Baquero, and Marek Zawirski. Conflict-free replicated data types)
*)

// test
//     "syncing is commutative"
//     (fun
//         ((addrA1, addrB1, addrA2, addrB2) : 
//             (Address * Address * Address * Address))
//         (eventsA : (Event.ID * int) list)
//         (eventsB : (Event.ID * int) list) ->
            
//         let rA1 = {Db = Database(); Addr = addrA1}
//         let rB1 = {Db = Database(); Addr = addrB1}

//         let rA2 = {Db = Database(); Addr = addrA2}
//         let rB2 = {Db = Database(); Addr = addrB2}

//         // A replicas have same events
//         rA1.Db.WriteEvents(None, eventsA)
//         rA2.Db.WriteEvents(None, eventsA)

//         // B replicas have same events
//         rB1.Db.WriteEvents(None, eventsB)
//         rB2.Db.WriteEvents(None, eventsB)

//         // Sync 1 & 2 in different order; a . b = b . a
//         send (acrossNetwork [rA1; rB1]) rB1 (Sync rA1.Addr)
//         send (acrossNetwork [rA2; rB2]) rA2 (Sync rB2.Addr)

//         converged rA1.Db rB2.Db
//     )

// test
//     "syncing is idempotent"
//     (fun
//         (addr: Address)
//         (events : (Event.ID * int) list) ->

//         let replica = {Addr = addr; Db = Database()}
//         let controlDb = Database()

//         replica.Db.WriteEvents(None, events)
//         controlDb.WriteEvents(None, events)

//         send (acrossNetwork [replica]) replica (Sync replica.Addr)

//         converged replica.Db controlDb
//     )

// test
//     "syncing is associative"
//     (fun
//         ((addrA1, addrB1, addrC1, addrA2, addrB2, addrC2) : 
//             (Address * Address * Address * Address * Address * Address))
//         (eventsA : (Event.ID * int) list)
//         (eventsB : (Event.ID * int) list)
//         (eventsC : (Event.ID * int) list) ->

//         let rA1 = {Db = Database(); Addr = addrA1}
//         let rB1 = {Db = Database(); Addr = addrB1}
//         let rC1 = {Db = Database(); Addr = addrC1}

//         let rA2 = {Db = Database(); Addr = addrA2}
//         let rB2 = {Db = Database(); Addr = addrB2}
//         let rC2 = {Db = Database(); Addr = addrC2}

//         // A replicas have same events
//         rA1.Db.WriteEvents(None, eventsA)
//         rA2.Db.WriteEvents(None, eventsA)

//         // B replicas have same events
//         rB1.Db.WriteEvents(None, eventsB)
//         rB2.Db.WriteEvents(None, eventsB)

//         // C replicas have same events
//         rC1.Db.WriteEvents(None, eventsC)
//         rC2.Db.WriteEvents(None, eventsC)

//         let acrossNetwork1 = acrossNetwork [rA1; rB1; rC1]
//         let acrossNetwork2 = acrossNetwork [rA2; rB2; rC2]

//         // (a . b) . c
//         send acrossNetwork1 rB1 (Sync rA1.Addr)
//         send acrossNetwork1 rA1 (Sync rC1.Addr)

//         // a . (b . c)
//         send acrossNetwork2 rC2 (Sync rB2.Addr) 
//         send acrossNetwork2 rB2 (Sync rA2.Addr)

//         converged rC1.Db rA2.Db
        
//     )
