module Laterbase.Simulated

open System

open System.Collections.Generic
open System.Threading.Tasks

open Laterbase.Core

open System.Collections.Generic
open System.Threading.Tasks

open Laterbase.Core

type Ether<'e> = SortedDictionary<Guid, Replica<'e>>

type Address<'e>(rng: Random, ether: Ether<'e>) =
    let randomBytes = Array.zeroCreate<byte> 16
    do
        rng.NextBytes randomBytes
    let addressId = Guid randomBytes
    
    interface IAddress<'e> with
        member _.Send (msg: Message<'e>) : Result<unit, Task<string>> = 
            match ether |> dictGet addressId with
            | Some(replica) -> 
                replica.Send msg |> ignore
                Ok ()
            | None -> Task.FromResult($"no replica for {addressId}") |> Error

    override _.ToString() = addressId.ToString()

type AddressFactory<'e>(rng: Random) = 
    let ether = Ether()
    member _.Create() = Address<'e> (rng, ether)
