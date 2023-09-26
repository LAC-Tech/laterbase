open System
open Laterbase
open Laterbase.Core

/// Deterministic Simulation Tester for Laterbase
/// Inspired by Tigerbeetle Simulator, as well as Will Wilsons talk.

let addrLen = 16 // TODO: arbitrary

/// TODO: move to simulated?
let randAddr (rng: Random) =
    let bytes = Array.zeroCreate<byte> addrLen
    rng.NextBytes bytes
    Address bytes

[<EntryPoint>]
let main args =
    // Can replay with a given seed if one is provided
    let seed = 
        match args with
        | [||] -> Random().Next()
        | [|s|] -> 
            try
                int s
            with | :? System.FormatException ->
                failwithf "First argument %A was not an integer" s
        | _ -> failwith "too many args"

    let rng = Random seed

    let (addr1, addr2) = (randAddr rng, randAddr rng)

    let replicas = Simulated.Replicas [|addr1; addr2|];

    // TODO: these IDs are not deterministc, use newId
    let newEvents = [
        Event.ID.Generate(), "Monday"; 
        Event.ID.Generate(), "Tuesday"
    ]

    replicas[0].Recv (StoreNew newEvents)
    
    Inspect.replica(replicas)

    0

(*
    for t in 0L<Time.ms> .. 10L<Time.ms> .. Time.s do
        printfn "%A miliseconds elapsed" t
*)
