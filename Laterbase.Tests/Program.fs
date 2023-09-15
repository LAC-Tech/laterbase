open Hedgehog
open Laterbase.Core
open System

Console.Clear ()


Range.constant 0 100
|> Gen.int32
|> Gen.renderSample
|> printfn "%s"

(*
let gen16Bytes: Gen<byte array> = 
    Arb.generate<byte> |> Gen.arrayOfLength 16

let genLogicalClock: Gen<Clock.Logical> = 
    Arb.generate<int> |> Gen.map (abs >> Clock.Logical.FromInt)

let genEventID: Gen<Event.ID> =
    Gen.map2 
        (fun ts bytes -> Event.ID(ts, bytes))
        (Arb.generate<int64<Time.ms>> |> Gen.map abs)
        (Arb.generate<byte> |> Gen.arrayOfLength 10)

let genDb: Gen<Database<byte>> = 
    Arb.generate<unit> |> Gen.map (fun _ -> Database<byte>())

let genAddr: Gen<Address> = 
    gen16Bytes |> Gen.map (fun bytes -> {id = bytes})

type MyGenerators = 
    static member LogicalClock() = 
        {new Arbitrary<Clock.Logical>() with
            override _.Generator = genLogicalClock
            override _.Shrinker _ = Seq.empty}
    
    static member EventID() =
        {new Arbitrary<Event.ID>() with
            override _.Generator = genEventID                            
            override _.Shrinker _ = Seq.empty}

    static member Database() =
        {new Arbitrary<Database<byte>>() with
            override _.Generator = genDb
            override _.Shrinker _ = Seq.empty}

    static member Address() =
        {new Arbitrary<Address>() with
            override _.Generator = genAddr
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
*)

// let merge (addr1: Address<EventVal>) (addr2: Address<EventVal>) =
//     send addr1 addr2 Sync
//     send addr2 addr1 Sync

// let commutative 
//     ((addrL1, addrR1): (Address<byte> * Address<byte>))
//     ((addrL2, addrR2): (Address<byte> * Address<byte>)) =

//     merge addrL1 addrL2
//     merge addrR2 addrR1

//     addrL1 = addrR2

//Check.One(config, commutative)

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
