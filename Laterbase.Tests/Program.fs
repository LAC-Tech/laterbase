open FsCheck
open Laterbase.Core
open System

Console.Clear ()

type EventVal = byte

let gen16Bytes = Arb.generate<byte> |> Gen.arrayOfLength 16

let genLogicalClock = Arb.generate<uint32<events>>

let genEventID: Gen<Event.ID> =
    Gen.map2 
        (fun ts bytes -> Event.ID(ts, bytes))

        (Arb.generate<int64<Time.ms>> |> Gen.map abs)
        (Arb.generate<byte> |> Gen.arrayOfLength 10)

let genStorage<'a> = Arb.generate<unit> |> Gen.map (fun _ -> Storage<'a, 'a>())

let genDb = Arb.generate<unit> |> Gen.map (fun _ -> Database<byte>())

let genAddr = gen16Bytes |> Gen.map Address

let genReplica = 
    Gen.map2
        (fun db addr -> {Db = db; Addr = addr})
        genDb
        genAddr

type MyGenerators = 
    static member EventID() =
        {new Arbitrary<Event.ID>() with
            override _.Generator = genEventID                            
            override _.Shrinker _ = Seq.empty}

    static member Storage<'a>() =
        {new Arbitrary<Storage<'a, 'a>>() with
            override _.Generator = genStorage
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
    "ID's are unique"
    (fun (eventIds: Event.ID list) ->
        (eventIds |> List.distinct |> List.length) = (eventIds |> List.length))

(** TODO: this sometimes fails with (StdGen (1022952468, 297233842)) *)
test
    "Storing events locally is idempotent"
    (fun (inputEvents: (int64 * int64) list) ->
        let storage = Storage()
        seq {
            for _ in 1..100 do
                storage.WriteEvents inputEvents

                let (outputEvents, _) = storage.ReadEvents 0UL

                let outputEvents = outputEvents |> List.ofSeq

                let inputEvents = 
                    inputEvents |> List.distinctBy (fun (k, _) -> k) 

                let result = inputEvents = outputEvents

                if (not result) then
                    eprintfn "ERROR: %A != %A" inputEvents outputEvents

                result
        }
        |> Seq.forall id
    )

// test 
//     "two databases will have the same events if they sync with each other"
//     (fun 
//         ((r1, events1) : (Replica<EventVal> * (Event.ID * EventVal) list))
//         ((r2, events2) : (Replica<EventVal> * (Event.ID * EventVal) list))
//         ->

//         // Simulating a network
//         let network = Map.ofList [r1.Addr, r1.Db; r2.Addr, r2.Db]

//         let rec send addr (msg: Message<byte>) =
//             let db = network |> Map.find addr
//             let replica = {Db = db; Addr = addr}
//             recv replica send msg

//         // Populate the two databases with separate events
//         r1.Db.WriteEvents None events1
//         r2.Db.WriteEvents None events2

//         recv r1 send (Sync r2.Addr)
//         recv r2 send (Sync r1.Addr)

//         r1.Db = r2.Db
//     )
