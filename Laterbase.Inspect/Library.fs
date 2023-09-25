namespace Laterbase.Inspect

open System
open Terminal.Gui
open Laterbase.Core

type Replica<'e>(replica: IReplica<'e>) =
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
