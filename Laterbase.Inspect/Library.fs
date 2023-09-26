module Laterbase.Inspect

open System
open Terminal.Gui
open Laterbase.Core

type private Replica<'e>(replica: IReplica<'e>) =
    inherit TabView(
        X = 0,
        Y = Pos.Percent(50.0f),
        Width = Dim.Fill(),
        Height = Dim.Fill()
    )

    do 
        let view = replica.View()

        let events = view.Events

        let eventsDt = new Data.DataTable()
        eventsDt.Columns.Add "ID" |> ignore
        eventsDt.Columns.Add "Origin" |> ignore
        eventsDt.Columns.Add "Payload" |> ignore

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

let replica (rs: IReplica<'e> array) =
    runView (fun () -> 
        let addresses = rs |> Array.map (fun r -> r.Addr)

        let replicaList = new FrameView(
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Percent(50.0f)
        )

        replicaList.Add(new ListView(
            addresses,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        ))

        let window = new Window(
            "Replica Inspector",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        )

        window.Add(replicaList, new Replica<'e>(rs[0]))
        window
    )
    
