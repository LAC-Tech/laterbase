open FsCheck
open Laterbase.Core
open System
open System.Collections.Generic

Console.Clear ()

type EventVal = byte

// Everything gets sent everywhere immediately with no isses :)
type ReplicaFactory<'e>() =
    let ether = SortedDictionary<byte array, Replica<'e>>()
    member _.Create() =
        let addr = { new Address<_>(Guid().ToByteArray()) with
            override this.Send(msg) = 
                match ether |> dictGet (this.Bytes) with
                | Some replica -> replica.Send msg
                | _ -> failwith "TODO: testing missing addresses"
        }

        let replica = Replica(addr)
        ether.Add(addr.Bytes, replica)

        replica

let genLogicalClock = 
    Arb.generate<int> |> Gen.map (abs >> Clock.Logical.FromInt)

let genEventID =
    Gen.map2 
        (fun ts (bytes: byte array) -> Event.createID ts (ReadOnlySpan bytes))
        (Arb.generate<int64<Time.ms>> |> Gen.map abs)
        (Arb.generate<byte> |> Gen.arrayOfLength 10)

let genDb = Arb.generate<Guid> |> Gen.map (Database<EventVal, Guid>)

type MyGenerators = 
    static member LogicalClock() = 
        {new Arbitrary<Clock.Logical>() with
            override _.Generator = genLogicalClock
            override _.Shrinker _ = Seq.empty}
    
    static member EventID() =
        {new Arbitrary<Event.ID>() with
            override _.Generator = genEventID                            
            override _.Shrinker _ = Seq.empty}

    static member DataBase() =
        {new Arbitrary<Database<EventVal, Guid>>() with
            override _.Generator = genDb
            override _.Shrinker _ = Seq.empty}

let config = {
    Config.Quick with Arbitrary = [ typeof<MyGenerators> ]
}

let logicaClockToAndFromInt (lc: Clock.Logical) =
    let i = lc.ToInt() 
    i = Clock.Logical.FromInt(i).ToInt()

Check.One(config, logicaClockToAndFromInt)

let idsAreUnique (eventIds: List<Event.ID>) =
    (eventIds |> List.distinct |> List.length) = (eventIds |> List.length)

Check.One(config, idsAreUnique)

let ``storing events locally is idempotent`` 
    (db: Database<EventVal, Guid>) 
    (es: (Event.ID * byte) list) =
    db.WriteEvents None es

    let (actualEvents, lc) = 
        db.ReadEvents (Time.Transaction Clock.Logical.Epoch)

    es = actualEvents

Check.One(config, ``storing events locally is idempotent``)
