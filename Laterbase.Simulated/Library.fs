module Laterbase.Simulated

open System
open System.Collections.Generic
open System.Threading.Tasks

open Laterbase.Core

/// In-memory replicas that send messages immediately
/// TODO: "you can make it not so easy..."
let Replicas<'e> addrs =
    let network = ResizeArray<IReplica<'e>>()
    let sendMsg addr = network.Find(fun r -> r.Addr = addr).Recv
    let addrToReplica addr = LocalReplica(addr, sendMsg) :> IReplica<'e>
    let rs = Array.map addrToReplica addrs
    network.AddRange(rs)
    rs
