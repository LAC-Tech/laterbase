module Laterbase.Inspect

open System
open System.Threading.Tasks
open Terminal.Gui
open Laterbase.Core

type private Replica<'e>(replicaView: View<'e>) =
    inherit TabView(
        X = 0,
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill()
    )

    do 
        let view = replicaView
        let events = view.Events

        let eventsDt = new Data.DataTable()

        for colName in ["ID"; "Origin"; "Payload"] do
            eventsDt.Columns.Add colName |> ignore

        for (k, origin, payload) in events do
            eventsDt.Rows.Add(k, origin, payload) |> ignore

        let eventsView = new TableView(
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Table = eventsDt
        )

        base.AddTab(TabView.Tab("Events", eventsView), true)

        match view.Debug with
        | Some (viewData) -> 
            let appendLogView = new ListView(
                viewData.AppendLog |> Seq.toArray,
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            )

            let logicalClockDt = new Data.DataTable()

            for colName in [|"Address"; "Sent"; "Received"|] do
                logicalClockDt.Columns.Add colName |> ignore

            for (addr, sent, received) in viewData.LogicalClock do
                logicalClockDt.Rows.Add(addr, sent, received) |> ignore

            let logicalClockView = new TableView(
                logicalClockDt,
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            )

            base.AddTab(TabView.Tab("AppendLog", appendLogView), false)
            base.AddTab(TabView.Tab("LogicalClock", logicalClockView), false)
        | _ -> ()

let runView (createViewArray: unit -> View) =
    Application.Init()

    let vs = createViewArray ()
    Application.Top.Add(vs)
    Application.Run()
    // This will not be reached on watch mode.
    // Make sure to always quit with Ctrl-Q before re-running.
    Application.Shutdown()

let replicas (rs: IReplica<'e> array) =
    runView (fun () -> 
        let addresses = rs |> Array.map (fun r -> r.Addr)

        let replicaListFrame = new FrameView(
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Percent(25.0f)
        )

        let replicaList = new ListView(
            addresses,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        )

        replicaListFrame.Add replicaList

        let window = new Window(
            "Replica Inspector",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        )

        let replicaFrame = new FrameView(
            X = 0,
            Y = Pos.Percent(25.0f),
            Width = Dim.Fill(),
            Height = Dim.Percent(75.0f)
        )

        rs[0].View() |> Task.iter (fun rv -> 
            replicaFrame.Add (new Replica<'e>(rv))
        )

       

        replicaList.add_SelectedItemChanged(fun args ->
            let addr = args.Value :?> Address
            let replica = rs |> Array.find (fun r -> r.Addr = addr)

            replica.View() |> Task.iter (fun rv -> 
                replicaFrame.Add (new Replica<'e>(rv))
            )
        )

        window.Add(replicaListFrame, replicaFrame)
        window
    )   
