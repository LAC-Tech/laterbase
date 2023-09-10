﻿open FsCheck
open Laterbase.Core
open System
open System.Collections.Generic

Console.Clear ()

type EventVal = byte

// Everything gets sent everywhere immediately with no isses :)
type AddressFactory<'e>() =
    let ether = SortedDictionary<byte array, Replica<'e>>()
    member _.Create(randBytes) =
        { new Address<_>(randBytes) with
            override this.Send msg = 
                match ether |> dictGet (this.Bytes) with
                | Some replica -> replica.Send msg
                | _ -> failwith "TODO: testing missing addresses"
            override this.CreateReplica() = 
                let r = Replica(this, Database())
                ether.Add(this.Bytes, r)
                r
        }

let gen16Bytes = Arb.generate<byte> |> Gen.arrayOfLength 16

let genReplicaPair =
    let addressFactory = AddressFactory<byte>()
    let f (bs1, bs2) (es1, es2) =
        let (addr1, addr2) = 
            (addressFactory.Create bs1, addressFactory.Create bs2)


        let (r1, r2) = (addr1.CreateReplica(), addr2.CreateReplica())

        r1.Send(StoreEvents(None, es1))
        r1.Send(StoreEvents(None, es2))

        (r1, r2)

    
    Gen.map2 f 
        (Gen.two gen16Bytes) 
        Arb.generate<((Event.ID * byte) list * (Event.ID * byte) list)>

let genLogicalClock = 
    Arb.generate<int> |> Gen.map (abs >> Clock.Logical.FromInt)

let genEventID =
    Gen.map2 
        (fun ts (bytes: byte array) -> Event.createID ts (ReadOnlySpan bytes))
        (Arb.generate<int64<Time.ms>> |> Gen.map abs)
        (Arb.generate<byte> |> Gen.arrayOfLength 10)

type MyGenerators = 
    static member LogicalClock() = 
        {new Arbitrary<Clock.Logical>() with
            override _.Generator = genLogicalClock
            override _.Shrinker _ = Seq.empty}
    
    static member EventID() =
        {new Arbitrary<Event.ID>() with
            override _.Generator = genEventID                            
            override _.Shrinker _ = Seq.empty}

    static member DB() =
        {new Arbitrary<Database<byte>>() with
            override _.Generator = 
                Arb.generate<unit> 
                |> Gen.map (fun _ -> Database())                            
            override _.Shrinker _ = Seq.empty}

    static member ReplicaPair() =
        {new Arbitrary<(Replica<byte> * Replica<byte>)>() with
            override _.Generator = genReplicaPair                            
            override _.Shrinker _ = Seq.empty}

let config = {
    Config.Quick with Arbitrary = [ typeof<MyGenerators> ]
}

let logicaClockToAndFromInt (lc: Clock.Logical) =
    let i = lc.ToInt() 
    i = Clock.Logical.FromInt(i).ToInt()

Check.One(config, logicaClockToAndFromInt)

let idsAreUnique (eventIds: Event.ID list) =
    (eventIds |> List.distinct |> List.length) = (eventIds |> List.length)

Check.One(config, idsAreUnique)

let ``storing events locally is idempotent`` 
    (db: Database<EventVal>)
    (inputEvents: (Event.ID * byte) list) =
    db.WriteEvents None inputEvents

    let (outputEvents, lc) = 
        db.ReadEvents (Time.Transaction Clock.Logical.Epoch)

    inputEvents = outputEvents

Check.One(config, ``storing events locally is idempotent``)

let merge (r1: Replica<byte>) (r2: Replica<byte>) =
    r1.Send(SyncWith(r2.Address))
    r2.Send(SyncWith(r1.Address))

let commutative 
    ((replicaL1, replicaR1): (Replica<byte> * Replica<byte>))
    ((replicaL2, replicaR2): (Replica<byte> * Replica<byte>)) =

    merge replicaL1 replicaL2
    merge replicaR2 replicaR1

    // TODO: pointless, just reference equality.
    // how to read state from 'local' replicas?
    replicaL1 = replicaR2

(*
#[test]
fn commutative(
    (mut db_left_a, mut db_right_a) in arb_db_pairs(),
    (mut db_left_b, mut db_right_b) in arb_db_pairs(),
) {
    merge(&mut db_left_a, &mut db_left_b);
    merge(&mut db_right_b, &mut db_right_a);

    assert_eq!(db_left_a, db_right_b);
}
*)