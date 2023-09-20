open System
open System.Collections.Generic
open System.Diagnostics.Tracing
open System.Threading.Tasks

open Mindmagma.Curses

type MyCursesLibraryNames() =
    inherit CursesLibraryNames()
    override this.ReplaceLinuxDefaults with get() = true
    override this.NamesLinux with get() = List ["libncursesw.so"]

let failIfNotZero n =
    if n = 0 then failwithf "%A" n

[<EntryPoint>]
let main args =
    let Screen = NCurses.InitScreen()
    NCurses.AddString("HELLO FROM CURSES") |> ignore
    NCurses.Refresh() |> ignore
    NCurses.GetChar() |> ignore
    NCurses.EndWin()

    0

(*
open Laterbase.Core
open Laterbase.Simulated
/// Deterministic Simulation Tester for Laterbase
/// Inspired by Tigerbeetle Simulator, as well as Will Wilsons talk.

type Event = byte

let log (s: string) = printf $"{s}"

[<EntryPoint>]
let main args =
    Console.Clear()
    
    let seed = 
        match args with
        | [||] -> Random().Next()
        | [|s|] -> 
            try
                int s
            with | :? System.FormatException ->
                failwithf "First argument %A was not an integer" s
        | _ -> failwith "too many args"

    let rng = Random seed
    let addressFactory = AddressFactory<Event> seed

    let replicaCount = rng.Next(2, 16)

    let addresses = 
        seq { 0 .. replicaCount } 
        |> Seq.map (fun _ -> addressFactory.Create ())
        |> Seq.toList

    for addr in addresses do
        $"Replica created at address: {addr}\n" |> log

    for t in 0L<Time.ms> .. 10L<Time.ms> .. Time.s do
        printfn "%A miliseconds elapsed" t

    0
*)
