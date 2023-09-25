open System
open Laterbase
open Laterbase.Core
open Terminal.Gui

/// Deterministic Simulation Tester for Laterbase
/// Inspired by Tigerbeetle Simulator, as well as Will Wilsons talk.

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

type ReplicaInspector(replica: IReplica<'e>) =
    inherit Window(
        "Laterbase Inspector",
        X = 0,
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill()
    )

    do 
        let events = replica.Read({ByTime = PhysicalValid; Limit = 0uy})

        let tabs = new TabView(
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        )

        let eventsDt = new Data.DataTable()
        eventsDt.Columns.Add "ID" |> ignore
        eventsDt.Columns.Add "Value" |> ignore

        for (k, v) in events do
            eventsDt.Rows.Add(k, v) |> ignore

        let eventsView = new TableView(
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Table = eventsDt
        )

        tabs.AddTab(TabView.Tab("Events", eventsView), true)

        replica.Debug() |> Option.iter (fun viewData ->
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

            tabs.AddTab(TabView.Tab("AppendLog", appendLogView), false)
            tabs.AddTab(TabView.Tab("LogicalClock", logicalClockView), false)
        )



        base.Add tabs

// TODO: arbitrary
let addrLen = 16

let randAddr (rng: Random) =
    let bytes = Array.zeroCreate<byte> addrLen
    rng.NextBytes bytes
    Address bytes

[<EntryPoint>]
let main args =
    // Can replay with a given seed if one is provided
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

    let replicas = Simulated.Replicas [|randAddr rng; randAddr rng|];
    replicas[0].Recv (StoreNew [
        Event.ID.Generate(), "Monday"; 
        Event.ID.Generate(), "Tuesday"
    ])

    for e in replicas[0].Read({ByTime = PhysicalValid; Limit = 0uy}) do
        printfn $"{e}"

    
    (*
    Application.Init()

    let inspector = new ReplicaInspector(replicas[0])
    // TODO: there should be some standard keypress to stop this?
    Application.Top.add_KeyPress(fun args ->
        match args.KeyEvent.Key with
        | Key.Esc -> Application.RequestStop ()
        | _ -> ()
    )

    Application.Top.Add (inspector)
    Application.Run()
    Application.Shutdown()
    *)

    0

(*
    for t in 0L<Time.ms> .. 10L<Time.ms> .. Time.s do
        printfn "%A miliseconds elapsed" t
*)
