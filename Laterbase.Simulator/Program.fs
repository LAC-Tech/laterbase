open System
open System.Collections.Generic
open System.Diagnostics.Tracing
open System.Threading.Tasks

open Terminal.Gui

type Character(symbol: char, x: int, y: int) =
    inherit View()
    do
        base.Width <- 1
        base.Height <- 1
        base.X <- x
        base.Y <- y
        base.Text <- symbol.ToString()

    override this.Redraw(region: Rect) =
        base.SetNeedsDisplay()

type ExampleWindow() =
    inherit Window(
        "Roguelike Demo",
        X = 0,
        Y = 1,
        Width = Dim.Fill(),
        Height = Dim.Fill()
    )

    let map = new FrameView(
        X = 0,
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill()
    )

    let player = new Character('@', 10, 10)
    
    do
        map.Add player
        base.Add map
        
        base.add_KeyPress(fun args ->
            match args.KeyEvent.Key with
            | Key.Esc -> Application.RequestStop ()
            | _ -> ()
        )

[<EntryPoint>]
let main args =
    Application.Run<ExampleWindow>()
    Application.Shutdown ()

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
