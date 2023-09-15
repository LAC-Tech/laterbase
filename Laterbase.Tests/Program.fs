open FsCheck
open Laterbase.Core
open System

Console.Clear ()

type EventVal = byte

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

let genAddr: Gen<Address> = gen16Bytes |> Gen.map (fun bytes -> {id = bytes})

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

let test descr testFn =
    printfn "# %A" descr
    Check.One(config, testFn)
    printfn "\n"

test 
    "Can convert a logical clock to and from an int"
    (fun (lc: Clock.Logical) -> (
        let i = lc.ToInt() 
        i = Clock.Logical.FromInt(i).ToInt()
    ))

test 
    "ID's are unique"
    (fun (eventIds: Event.ID list) ->
        (eventIds |> List.distinct |> List.length) = (eventIds |> List.length))

test
    "Storing events locally is idempotent"
    (fun
        (db: Database<EventVal>)
        (inputEvents: (Event.ID * byte) list) ->


        db.WriteEvents inputEvents None

        let (outputEvents, lc) = 
            db.ReadEvents (Time.Transaction Clock.Logical.Epoch)

        inputEvents = outputEvents)
