/// Deterministic Simulation Tester for Laterbase
/// Inspired by Tigerbeetle Simulator, as well as Will Wilsons talk.
module Laterbase.Simulator

open System
open Laterbase
open Laterbase.Core

type Range = {Min: int; Max: int}

module Range =
    let addrLen = {Min = 16; Max = 16} // Unique but still fits in screen?
    let replicaCount = {Min = 2; Max = 8} // TB has 0..8. TODO: Why 0?
    // How many events a replica will consume
    let eventsPerReplica = {Min = 0; Max = 256}
    let eventsPerTick = {Min = 0; Max = 8}

let randInt (rng: Random) (range: Range) =
    rng.Next(range.Min, range.Max)

type Probability = {Min: float; Max: float}

module Probability =
    let sync = {Min = 0.0; Max = 0.1}
    let recvEvents = {Min = 0.0; Max = 1.0}

// probability is the chance of it being *true*
let randBool (rng: Random) (prob: Probability) =
    let randFloat = rng.NextDouble() * prob.Max + prob.Min
    rng.NextDouble() < randFloat

let randByteArray (rng: Random) len =
    let result = Array.zeroCreate<byte> len
    rng.NextBytes result
    result
    
let randAddr (rng: Random) =
    let len = randInt rng Range.addrLen
    let bytes = randByteArray rng len
    Address bytes

let randElem<'a> (rng: Random) (elems: 'a array) =
    let index = rng.Next(0, elems |> Array.length)
    elems[index]

let randNewEvent (rng: Random) time =
    let _id = Event.newId time (randByteArray rng 10)
    let payload = rng.Next()
    (_id, payload)

let simTime = 10L<s> * msPerS

[<EntryPoint>]
let main args =
    // Can replay with a given seed if one is provided
    let seed = 
        match args with
        | [||] -> Random().Next()
        | [|s|] -> 
            try
                int s
            with | :? FormatException ->
                failwithf "First argument %A was not an integer" s
        | _ -> failwith "too many args"

    let rng = Random seed

    printfn "Stick it on the Laterbase"
    printfn $"Seed = {seed}"
    printfn $"Running for {simTime} ms"

    let numReplicas = randInt rng Range.replicaCount
    let addrs = Array.init numReplicas (fun _ -> randAddr rng)
    let replicas = Simulated.Replicas<int> addrs
    let eventsPerReplica = 
        Array.init numReplicas (fun _ -> randInt rng Range.eventsPerReplica)

    let stopWatch = new Diagnostics.Stopwatch()

    stopWatch.Start()

    for t in 0L<ms> .. 10L<ms> .. (10L<s> * msPerS) do
        for replica in replicas do
            if randBool rng Probability.sync then
                // TODO: could sync with self, is that OK?
                let destReplica = randElem rng replicas
                replica.Recv (Sync destReplica.Addr)

            if randBool rng Probability.recvEvents then
                let numEvents = randInt rng Range.eventsPerTick

                // TODO: allocating every loop..
                let newEvents = Array.init numEvents (fun _ -> 
                    // TODO: assuming transaction time = valid here
                    // TODO: all these times are the same
                    // TODO: Test forward-dating is forbidden
                    let t = t * 1L<valid>
                    randNewEvent rng t
                )

                replica.Recv (StoreNew newEvents)

    stopWatch.Stop()
    let ts = stopWatch.Elapsed

    printfn $"Simulation took {ts.Milliseconds} ms"
    printfn "View Replication Inspector? (y/n)"
    let k = Console.ReadKey(true)
    if k.KeyChar = 'y' then Inspect.replicas replicas
    Inspect.replicas(replicas)

    0

(*
    
*)
