open System

open Laterbase.Core

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

let mainLoop () =
    Application.Init()
    let runState = ref (Application.Begin (new ExampleWindow()))
    
    let firstIteration = ref true
    runState.Value.Toplevel.Running <- true

    let mutable time = 0L<ms>

    let rec loop () =
        let breakUiLoop =
            (not runState.Value.Toplevel.Running) ||
            (Application.ExitRunLoopAfterFirstIteration &&
            not firstIteration.Value)

        if breakUiLoop then
            ()
        else 
            Application.RunMainLoopIteration(runState, false, firstIteration)

            let dateTime = 
                DateTimeOffset.FromUnixTimeMilliseconds(int64 time).DateTime

            map.Title <- dateTime.ToString()
            map.SetNeedsDisplay()
            time <- time + 10L<ms>
            loop ()

    loop ()

type DBInspector(db: LocalDatabase<'e>) =
    inherit Window(
        "Laterbase Inspector",
        X = 0,
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill()
    )

    do 
        let viewData = db.View()

        let tabs = new TabView(
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        )

        let eventsDt = new Data.DataTable()
        eventsDt.Columns.Add "ID" |> ignore
        eventsDt.Columns.Add "Value" |> ignore

        for (k, v) in viewData.Events do
            eventsDt.Rows.Add(k, v) |> ignore

        let eventsView = new TableView(
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Table = eventsDt
        )

        let appendLogView = new ListView(
            viewData.AppendLog |> Seq.toArray,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        )

        let logicalClockDt = new Data.DataTable()
        logicalClockDt.Columns.Add "Address" |> ignore
        logicalClockDt.Columns.Add "Sent" |> ignore
        logicalClockDt.Columns.Add "Received" |> ignore
        for (addr, sent, received) in viewData.LogicalClock do
            logicalClockDt.Rows.Add(addr, sent, received) |> ignore


        let logicalClockView = new TableView(
            logicalClockDt,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        )

        tabs.AddTab(TabView.Tab("Events", eventsView), true)
        tabs.AddTab(TabView.Tab("AppendLog", appendLogView), false)
        tabs.AddTab(TabView.Tab("LogicalClock", logicalClockView), false)

        base.Add tabs



        
[<EntryPoint>]
let main _ =
    let db = LocalDatabase<string>()

    (db :> IDatabase<_>).WriteEvents(None, [
        Event.ID.Generate(), "Monday"; 
        Event.ID.Generate(), "Tuesday"
    ])

    Application.Init()
    Application.Top.add_KeyPress(fun args ->
        match args.KeyEvent.Key with
        | Key.Esc -> Application.RequestStop ()
        | _ -> ()
    )
    Application.Top.Add (new DBInspector(db))
    Application.Run()
    Application.Shutdown()

    //mainLoop () b
    //Application.Shutdown()



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
