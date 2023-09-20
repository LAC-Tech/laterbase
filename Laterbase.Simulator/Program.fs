open System

open Terminal.Gui

let speedMenu = 
    seq {"Pause"; "Turtle"; "Llama"; "Cheetah"; "Tiger Beetle"} 
    |> Seq.map (fun title -> MenuItem (Title = title))
    |> Seq.toArray
    |> fun items -> MenuBarItem("Speed", items)

let menuBar = new MenuBar [|speedMenu|]

type Replica(x: int, y: int) =
    inherit View(x, y, "ᚠ")

let map = new Window(
    "Laterbase",
    X = 0,
    Y = 1,
    Width = Dim.Fill(),
    Height = Dim.Fill()
)

type ExampleWindow() =
    inherit Toplevel(
        X = 0,
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill()
    )

    let replica1 = new Replica(10, 10)
    let replica2 = new Replica(20, 10)
    
    do
        map.Add replica1
        map.Add replica2
        base.Add map
        base.Add menuBar
        
        base.add_KeyPress(fun args ->
            match args.KeyEvent.Key with
            | Key.Esc -> Application.RequestStop ()
            | _ -> ()
        )

let updateGui count =
    map.Title <- $"Count: {count}"
    map.SetNeedsDisplay()

        
[<EntryPoint>]
let main _ =
    let mainLoop = task {
        let mutable counter = 0

        while true do
            do! Threading.Tasks.Task.Delay (TimeSpan.FromSeconds 1)

            counter <- counter + 1

            Application.MainLoop.Invoke(fun _ -> 
                map.Title <- $"Count: {counter}";
                map.SetNeedsDisplay() |> ignore
            )
    }

    mainLoop.Start(Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext())

    Application.Run<ExampleWindow>()
    Application.Shutdown()

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
