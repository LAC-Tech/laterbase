open System
open System.Collections.Generic
open System.Diagnostics.Tracing
open System.Threading.Tasks

open Library
/// Deterministic Simulation Tester for Laterbase
/// Heavily inspired by the Tigerbeetle Simulator, as well as Will Wilsons talk.

type Event = int32

let log (s: string) = printf $"{s}"

type Ether = SortedDictionary<Guid, Library.Replica<Event>>

type Address(rng: Random, ether: Ether) =
    let randomBytes = Array.zeroCreate<byte> 16
    do
        rng.NextBytes randomBytes
    let addressId = Guid randomBytes
    
    override _.ToString() = addressId.ToString()

    interface Library.IAddress<Event> with
        member _.Send msg : Result<unit, Threading.Tasks.Task<string>> = 
            match ether |> Library.dictGet addressId with
            | Some(replica) -> 
                replica.Send msg |> ignore
                Ok ()
            | None -> Task.FromResult($"no replica for {addressId}") |> Error

type AddressFactory<'e>(rng: Random) = 
    let ether = Ether()
    member _.Create() = Address (rng, ether)

[<EntryPoint>]
let main args =
    Console.Clear()
    
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
    let addressFactory = AddressFactory<Event> rng

    let replicaCount = rng.Next(2, 16)

    let replicas = 
        seq { 0 .. replicaCount } 
        |> Seq.map (fun _ -> addressFactory.Create () |> Library.Replica)


    for replica in replicas do
        $"{replica.Address}\n" |> log

        for t in 0L<Time.ms> .. 10L<Time.ms> .. Time.h do
            ()
    0
