module Laterbase.Simulated

open System
open System.Collections.Generic
open System.Threading.Tasks

open Laterbase.Core

let Replicas<'e> addrs =
    let network = ResizeArray<IReplica<'e>>()
    let sendMsg addr = network.Find(fun r -> r.Addr = addr).Recv
    let addrToReplica addr = LocalReplica(addr, sendMsg) :> IReplica<'e>
    let rs = Array.map addrToReplica addrs
    network.AddRange(rs)
    rs

(*
type Ether<'e> = SortedDictionary<Guid, Replica<'e>>

type Address<'e>(rng: Random, ether: Ether<'e>) =
    let randomBytes = Array.zeroCreate<byte> 16
    do
        rng.NextBytes randomBytes
    let addressId = Guid randomBytes
    
    interface IAddress<'e> with
        member _.Send (msg: Message<'e>) : Result<unit, Task<string>> = 
            match ether |> dictGet addressId with
            | Some replica -> replica.Send msg
            | None -> Task.FromResult($"no replica for {addressId}") |> Error

    member _.Id = addressId
    override _.ToString() = addressId.ToString()
    override _.Equals(other) = 
        match other with
        | :? Address<'e> as addr -> addressId = addr.Id
        | _ -> false
    override _.GetHashCode() = addressId.GetHashCode ()


type AddressFactory<'e>(seed: int) = 
    let ether = Ether()
    let rng = Random seed
    member _.Create() = 
        let addr = Address<'e> (rng, ether)
        ether[addr.Id] <- Replica addr
        addr
*)
