open System
open FsCheck
open FsCheck.FSharp
open Laterbase
open Laterbase.Core

Console.Clear ()

let gen16Bytes = 
    ArbMap.defaults |> ArbMap.generate<byte> |> Gen.arrayOfLength 16

let genEventID =
    Gen.map2 
        (fun ts bytes -> EventID(ts, bytes))
        (ArbMap.defaults |> ArbMap.generate<int64<valid ms>> |> Gen.map abs)
        (ArbMap.defaults |> ArbMap.generate<byte> |> Gen.arrayOfLength 10)

let genAddr = gen16Bytes |> Gen.map Address

type MyGenerators = 
    static member EventID() =
        {new Arbitrary<EventID>() with
            override _.Generator = genEventID                            
            override _.Shrinker _ = Seq.empty}

    static member Address() =
        {new Arbitrary<Address>() with
            override _.Generator = genAddr
            override _.Shrinker _ = Seq.empty}

let config = Config.Quick.WithArbitrary([ typeof<MyGenerators> ])

let openInspector rs =
    printfn "error - open inspector? (y/n)"
    let k = Console.ReadKey(true)
    if k.KeyChar = 'y' then Inspect.replicas rs

let test descr testFn =
    printfn "# %A" descr
    Check.One(config, testFn)
    printfn "\n"

test
    "Can read back the events you store"
    (fun (inputEvents: (EventID * int64) array) (addr: Address) ->
        let r = localReplica(addr, fun _ _ -> ())
        let equalityReults = seq {
            for _ in 1..100 do
                r.Recv (StoreNew inputEvents)

                // Replica will not store duplicates
                let inputEvents = 
                    inputEvents |> Array.distinctBy (fun (k, _) -> k) 

                let outputEvents = r.Read({ByTime = LogicalTxn; Limit = 0})

                task {
                    let! outputEvents = outputEvents
                    let outputEvents = 
                        outputEvents 
                        |> Seq.map (fun (k, v) -> (k, v.Payload))
                        |> Seq.toArray

                    return inputEvents = outputEvents
                }
        }

        task {
            let! x = equalityReults |> System.Threading.Tasks.Task.WhenAll
            return x |> Seq.forall id
        }
    )

// /// In-memory replicas that send messages immediately
// let replicaNetwork<'e> addrs =
//     let network = ResizeArray<IReplica<'e>>()
//     let sendMsg addr = network.Find(fun r -> r.Addr = addr).Recv
//     let replicas = addrs |> Array.map (fun addr -> localReplica(addr, sendMsg))
//     network.AddRange(replicas)
//     replicas

// let oneTestReplica addr = replicaNetwork [|addr|].[0]

// let twoTestReplicas (addr1, addr2) =
//     let rs = replicaNetwork [|addr1; addr2|]
//     (rs[0], rs[1])

// let threeTestReplicas (addr1, addr2, addr3) =
//     let rs = replicaNetwork  [|addr1; addr2; addr3|]
//     (rs[0], rs[1], rs[2])

// (*
//     Sometimes I create two different networks where replicas have the same event payloads. So each have their own state and I can test algebraic properties.

//     In this scenario, I expect the payload values to converge, but the origin addresses will of course be different.
// *)
// type Connection = SameNetwork | DifferentNetworks

// let replicasConverged connection (r1: IReplica<'e>) (r2: IReplica<'e>) =
//     let query = {ByTime = PhysicalValid; Limit = 0}

//     let es1 = r1.Read(query)
//     let es2 = r2.Read(query)

//     let converged =
//         match connection with
//         | SameNetwork -> Seq.equal es1 es2
//         | DifferentNetworks -> 
//             let es1 = es1 |> Seq.map (fun (k, v) -> (k, v.Payload))
//             let es2 = es2 |> Seq.map (fun (k, v) -> (k, v.Payload))

//             Seq.equal es1 es2
    
//     if not converged then
//         eprintfn "Replicas did not converge"
//         openInspector [|r1; r2|]

//     converged

// test 
//     "two databases will have the same events if they sync with each other"
//     (fun
//         ((addr1, addr2) : (Address * Address))
//         (events1 : (EventID * int) array)
//         (events2 : (EventID * int) array) ->
    
//         let (r1, r2) = twoTestReplicas(addr1, addr2)

//         // Populate the two databases with separate events
//         r1.Recv(StoreNew events1)
//         r2.Recv(StoreNew events2)

//         // Bi-directional sync
//         r1.Recv(Sync r2.Addr)
//         r2.Recv(Sync r1.Addr)

//         replicasConverged SameNetwork r1 r2        
//     )

// (*
//     Merging state-based CRDTs should be
//     - commutative
//     - idempotent
//     - associative

//     (Definition 2.3, Marc Shapiro, Nuno Preguiça, Carlos Baquero, and Marek Zawirski. Conflict-free replicated data types)
// *)

// test
//     "syncing is commutative"
//     (fun
//         ((addrA1, addrB1, addrA2, addrB2) : 
//             (Address * Address * Address * Address))
//         (eventsA : (EventID * int) array)
//         (eventsB : (EventID * int) array) ->

//         let (rA1, rB1) = twoTestReplicas(addrA1, addrB1)
//         let (rA2, rB2) = twoTestReplicas(addrA2, addrB2)

//         // A replicas have same events
//         rA1.Recv(StoreNew eventsA)
//         rA2.Recv(StoreNew eventsA)

//         // B replicas have same events
//         rB1.Recv(StoreNew eventsB)
//         rB2.Recv(StoreNew eventsB)

//         // Sync 1 & 2 in different order; a . b = b . a
//         rB1.Recv(Sync rA1.Addr)
//         rA2.Recv(Sync rB2.Addr)

//         replicasConverged DifferentNetworks rA1 rB2
//     )

// test
//     "syncing is idempotent"
//     (fun
//         (addr: Address)
//         (controlAddr: Address)
//         (events : (EventID * EventVal<int>) array) ->

//         let (replica, controlReplica) = twoTestReplicas(addr, controlAddr)

//         replica.Recv (StoreNew events)
//         controlReplica.Recv (StoreNew events)

//         replica.Recv (Sync replica.Addr)

//         replicasConverged DifferentNetworks replica controlReplica
//     )

// test
//     "syncing is associative"
//     (fun
//         ((addrA1, addrB1, addrC1, addrA2, addrB2, addrC2) : 
//         (Address * Address * Address * Address * Address * Address))
//         (eventsA : (EventID * int) array)
//         (eventsB : (EventID * int) array)
//         (eventsC : (EventID * int) array) ->

//         let (rA1, rB1, rC1) = threeTestReplicas(addrA1, addrB1, addrC1)
//         let (rA2, rB2, rC2) = threeTestReplicas(addrA2, addrB2, addrC2)

//         // A replicas have same events
//         rA1.Recv (StoreNew eventsA)
//         rA2.Recv (StoreNew eventsA)

//         // B replicas have same events
//         rB1.Recv (StoreNew eventsB)
//         rB2.Recv (StoreNew eventsB)

//         // C replicas have same events
//         rC1.Recv (StoreNew eventsC)
//         rC2.Recv (StoreNew eventsC)

//         // (a . b) . c
//         rB1.Recv (Sync rA1.Addr)
//         rA1.Recv (Sync rC1.Addr)

//         // a . (b . c)
//         rC2.Recv (Sync rB2.Addr)
//         rB2.Recv (Sync rA2.Addr)

//         replicasConverged DifferentNetworks rC1 rA2
//     )