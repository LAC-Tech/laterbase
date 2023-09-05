open System
open System.Collections.Generic
open System.Diagnostics

let log (s: string) = printf $"{s}"

type AddressFactory<'e>(seed: int) = 
    let Rng = Random seed
    let ether = SortedDictionary<Guid, Library.Replica<'e>>()
    member _.Create() = 
        let randomBytes = Array.zeroCreate<byte> 16
        Rng.NextBytes randomBytes

        let eventId = Guid randomBytes
        { new Library.IAddress<'e> with
            member _.Send msg = 
                match ether |> Library.dictGet eventId with
                | Some(replica) -> replica.Send msg
                | None -> 
                    fprintfn Console.Error "no replica for %A" eventId }
type Event = int32

[<EntryPoint>]
let main args =
    let seed = 
        match args with
        | [||] -> Random().Next()
        | [|s|] -> 
            try
                int s
            with | :? System.FormatException as ex ->
                failwithf "First argument %A was not an integer" s

        | _ -> failwith "too many args"

    let addressFactory = AddressFactory<Event>(seed)

    0
