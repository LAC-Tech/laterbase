open System
open System.Collections.Generic

let seed = Random().Next()

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
let addressFactory = AddressFactory<Event>(seed)

// For more information see https://aka.ms/fsharp-console-apps
printfn "Hello from F#"