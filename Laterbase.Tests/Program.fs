﻿open FsCheck
open Laterbase.Core
open System

Console.Clear ()

type EventVal = byte

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

let ``storing events locally is idempotent`` (db: Database<EventVal, Guid>) (es: (Event.ID * byte) list) =
    db.WriteEvents None es

    printfn "%A" es

    let (readBackEvents, lc) = db.ReadEvents (Time.Transaction Clock.Logical.Epoch)

    es = readBackEvents

Check.One(config, ``storing events locally is idempotent``)