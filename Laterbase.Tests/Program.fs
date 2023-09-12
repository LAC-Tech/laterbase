open FsCheck
open Laterbase.Core
open System

Console.Clear ()

type EventVal = byte

let gen16Bytes = Arb.generate<byte> |> Gen.arrayOfLength 16

let genLogicalClock = 
    Arb.generate<int> |> Gen.map (abs >> Clock.Logical.FromInt)

let genEventID =
    Gen.map2 
        (fun ts (bytes: byte array) -> Event.ID(ts, (ReadOnlySpan bytes)))
        (Arb.generate<int64<Time.ms>> |> Gen.map abs)
        (Arb.generate<byte> |> Gen.arrayOfLength 10)

let genDb = Arb.generate<unit> |> Gen.map Database

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
            override _.Generator = genDb
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
    db.WriteEvents inputEvents None

    let (outputEvents, lc) = 
        db.ReadEvents (Time.Transaction Clock.Logical.Epoch)

    inputEvents = outputEvents

Check.One(config, ``storing events locally is idempotent``)

let merge (addr1: Address<EventVal>) (addr2: Address<EventVal>) =
    send addr1 addr2 SyncWith
    send addr2 addr1 SyncWith

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
