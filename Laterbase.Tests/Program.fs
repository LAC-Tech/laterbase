open FsCheck
open Laterbase.Core
open Laterbase.Simulated
open System

open FsCheck.Gen

Console.Clear ()

let genLogicalClock: Gen<Clock.Logical> = 
    Arb.generate<int>
    |> Gen.map (fun i -> i |> Math.Abs |> Clock.Logical.FromInt)

type MyGenerators = 
    static member LogicalClock() = 
        {new Arbitrary<Clock.Logical>() with
            override _.Generator = genLogicalClock
            override _.Shrinker _ = Seq.empty}
            
let config = {
    Config.Quick with 
        Arbitrary = [ typeof<MyGenerators>]
}

let logicaClockToAndFromInt (lc: Clock.Logical) =
    let i = lc.ToInt() 
    i = Clock.Logical.FromInt(i).ToInt()

Check.One(config, logicaClockToAndFromInt)
