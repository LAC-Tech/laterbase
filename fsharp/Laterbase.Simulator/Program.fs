/// Deterministic Simulation Tester for Laterbase
/// Inspired by Tigerbeetle Simulator, as well as Will Wilsons talk.
module Laterbase.Simulator

open System
open Laterbase
open Laterbase.Core

type Range = {Min: int; Max: int}

module Range =
    let addrLen = {Min = 16; Max = 16} // Unique but still fits in screen?
    let replicaCount = {Min = 2; Max = 10} // TB has 0..8. TODO: Why 0?
    // How many events a replica will consume
    let eventsPerReplica = {Min = 0; Max = 256}
    let eventsPerTick = {Min = 0; Max = 8}

type Probability = {Min: float; Max: float}

module Probability =
    let sync = {Min = 0.0; Max = 0.1}
    let recvEvents = {Min = 0.0; Max = 1.0}

module Rand =
    let int (rng: Random) (range: Range) =
        rng.Next(range.Min, range.Max)

    /// chance of it being true
    let bool (rng: Random) (prob: Probability) =
        let randFloat = rng.NextDouble() * prob.Max + prob.Min
        rng.NextDouble() < randFloat

    let byteArray (rng: Random) len =
        let result = Array.zeroCreate<byte> len
        rng.NextBytes result
        result
    
    let addr (rng: Random) =
        let len = int rng Range.addrLen
        let bytes = byteArray rng len
        Address bytes

    let elem<'a> (rng: Random) (elems: 'a array) =
        let index = rng.Next(0, elems |> Array.length)
        elems[index]

    let newEvent (rng: Random) time =
        let _id = EventID(time, (byteArray rng 10))
        let payload = rng.Next()
        (_id, payload)

type Stats = {
    mutable TotalMessages: uint64
    mutable NewEvents: uint64
    mutable EventsSent: ResizeArray<uint64>;
}

/// In-memory replicas that send messages immediately
/// TODO: "you can make it not so easy..."
let replicaNetwork<'e> addrs =
    let stats = {
        TotalMessages = 0UL
        NewEvents = 0UL
        EventsSent = ResizeArray()
    }
    let network = ResizeArray<IReplica<'e>>()
    let sendMsg addr msg =
        stats.TotalMessages <- stats.TotalMessages + 1UL

        match msg with
        | StoreNew newEvents -> 
            stats.NewEvents <- stats.NewEvents + (newEvents |> Array.uLength)
        | Store (es, _, _) ->
            stats.EventsSent.Add(Array.uLength es)
        | _ -> ()

        network.Find(fun r -> r.Addr = addr).Recv msg

    let replicas = 
        addrs |>
        Array.map (fun addr -> localReplica(addr, sendMsg)) 
    network.AddRange(replicas)
    (replicas, sendMsg, stats)

let simTime =  1L<m> * sPerM * msPerS

[<EntryPoint>]
let main args =
    // Can replay with a given seed if one is provided
    let seed = 
        match args with
        | [||] -> Random().Next()
        | [|s|] -> 
            try int s
            with | :? FormatException ->
                failwithf "First argument %A was not an integer" s
        | _ -> failwith "too many args"

    let rng = Random seed

    printfn $"Seed = {seed}"
    printfn "Sticking it on the Laterbase...\n"

    let numReplicas = Rand.int rng Range.replicaCount
    let addrs = Array.init numReplicas (fun _ -> Rand.addr rng)
    let (replicas, sendMsg, stats) = replicaNetwork<int> addrs
    let eventsPerReplica = 
        Array.init numReplicas (fun _ -> Rand.int rng Range.eventsPerReplica)

    let stopWatch = new Diagnostics.Stopwatch()
    stopWatch.Start()

    //let eventBuffer = Array.zeroCreate<EventID * int> Range.eventsPerTick.Max

    let rec replicaExcept r = 
        let result = Rand.elem rng replicas
        if result = r then
            replicaExcept r
        else
            result

    for t in 0L<ms> .. 10L<ms> .. simTime do
        for replica in replicas do
            if Rand.bool rng Probability.sync then
                let destReplica = replicaExcept replica
                sendMsg replica.Addr (Sync destReplica.Addr)

            if Rand.bool rng Probability.recvEvents then
                let numEvents = Rand.int rng Range.eventsPerTick

                // TODO: allocating every loop..
                let newEvents = Array.init numEvents (fun _ -> 
                    // TODO: assuming transaction time = valid here
                    // TODO: all these times are the same
                    // TODO: Test forward-dating is forbidden
                    Rand.newEvent rng (t * 1L<valid>)
                )

                sendMsg replica.Addr (StoreNew newEvents)

    stopWatch.Stop()
    let ts = stopWatch.Elapsed

    let simTimeSpan = TimeSpan.FromMilliseconds((Checked.int simTime))
    printfn "Simulation is complete."
    let distinctReplicas = 
        replicas 
        |> Seq.distinctBy (fun r -> r.Read({ByTime = PhysicalValid; Limit = 0}))

    eprintfn $"Distinct replicas = {distinctReplicas |> Seq.length}"

    printfn $"Simulated time = {simTimeSpan}, Real time = {ts}"
    printfn $"Statistics for {Array.length replicas} replicas:"
    printfn $"- Messages sent = {stats.TotalMessages:n0}"
    printfn $"- Events generated = {stats.NewEvents:n0}"
    printfn $"- Events sent = {stats.EventsSent |> Seq.sum:n0}\t" 
    printfn $"- Avg events per msg = {stats.EventsSent |> Seq.averageBy float:n0}"
    printfn "View Replication Inspector? (y/n)"
    let k = Console.ReadKey(true)
    if k.KeyChar = 'y' then Inspect.replicas replicas

    0

(*
    
*)
