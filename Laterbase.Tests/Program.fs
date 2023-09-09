open FsCheck
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
                let r = Replica(this)
                ether.Add(this.Bytes, r)
                r
        }

let gen16Bytes = Arb.generate<byte> |> Gen.arrayOfLength 16

let genAddrPair =
    let addressFactory = AddressFactory<byte>()
    Gen.two gen16Bytes 
    |> Gen.map (fun (bs1, bs2) -> (
        addressFactory.Create bs1,
        addressFactory.Create bs2
    ))

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

    if inputEvents <> outputEvents then
        eprintfn "input events: %A" inputEvents
        eprintfn "output events: %A" outputEvents
        false
    else 
        true
        

Check.One(config, ``storing events locally is idempotent``)
