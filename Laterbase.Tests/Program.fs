open FsCheck
open Laterbase.Core
open System

Console.Clear ()

type EventVal = byte

let gen16Bytes = Arb.generate<byte> |> Gen.arrayOfLength 16

let genLogicalClock = 
    Arb.generate<int> |> Gen.map (abs >> Clock.Logical.FromInt)

let genEventID: Gen<Event.ID> =
    Gen.map2 
        (fun ts bytes -> Event.ID(ts, bytes))

        (Arb.generate<int64<Time.ms>> |> Gen.map abs)
        (Arb.generate<byte> |> Gen.arrayOfLength 10)

let genDb = Arb.generate<unit> |> Gen.map (fun _ -> Database<byte>())

let genAddr = gen16Bytes |> Gen.map (fun bytes -> {id = bytes})

let genReplica = 
    Gen.map2
        (fun db addr -> {Db = db; Addr = addr})
        genDb
        genAddr

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
        {new Arbitrary<Database<EventVal>>() with
            override _.Generator = genDb
            override _.Shrinker _ = Seq.empty}

    static member Address() =
        {new Arbitrary<Address>() with
            override _.Generator = genAddr
            override _.Shrinker _ = Seq.empty}

    static member Replica() =
        {new Arbitrary<Replica<EventVal>>() with
            override _.Generator = genReplica
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

(** TODO: this sometimes fails WRITE DOWN THE SEED *)
test
    "Storing events locally is idempotent"
    (fun (db: Database<EventVal>) (inputEvents: (Event.ID * byte) list) ->
        seq {
            for _ in 1..100 do
                db.WriteEvents None inputEvents

                let (outputEvents, _) = 
                    db.ReadEvents (Time.Transaction Clock.Logical.Epoch)

                yield inputEvents = outputEvents
        } 
        |> Seq.forall id
    )

test 
    "two databases will have the same events if they sync with each other"
    (fun 
        ((r1, events1) : (Replica<EventVal> * (Event.ID * EventVal) list))
        ((r2, events2) : (Replica<EventVal> * (Event.ID * EventVal) list))
        ->

        // Simulating a network
        let network = Map.ofList [r1.Addr, r1.Db; r2.Addr, r2.Db]

        let rec send addr (msg: Message<byte>) =
            let db = network |> Map.find addr
            let replica = {Db = db; Addr = addr}
            recv replica send msg

        // Populate the two databases with separate events
        r1.Db.WriteEvents None events1
        r2.Db.WriteEvents None events2

        recv r1 send (Sync r2.Addr)
        recv r2 send (Sync r1.Addr)

        r1.Db = r2.Db
    )
