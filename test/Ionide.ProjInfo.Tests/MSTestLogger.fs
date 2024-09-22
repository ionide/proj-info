module MSTestLogger

open Microsoft.Extensions.Logging
open System.Threading.Channels
open System.Diagnostics
open Microsoft.VisualStudio.TestTools.UnitTesting
open System
open System.Threading.Tasks

// [<DebuggerStepThrough>]
type MSTestLogger(logs: ChannelWriter<string>, categoryName: string) =

    interface ILogger with
        member this.BeginScope(state: _): IDisposable =
            { new IDisposable with
                member this.Dispose() = () }

        member this.IsEnabled(logLevel: LogLevel): bool =
            true

        member this.Log(logLevel: LogLevel, eventId: EventId, state: 'TState, e: exn, formatter: Func<'TState,exn,string>): unit =
            logs.TryWrite($"{logLevel}: {categoryName} [{eventId}] - {formatter.Invoke(state, e)}")
            |> ignore<bool>

            if e <> null
            then
                logs.TryWrite(e.ToString())
                |> ignore<bool>

[<DebuggerStepThrough>]
type MSTestLoggerFactory(content: TestContext) =
    let logs : Channel<string> = Channel.CreateUnbounded<string>()

    do
        Task.Run(fun () ->
            task {
                while true do
                    let! message = logs.Reader.ReadAsync()
                    content.WriteLine(message)
            } :> Task
        )
        |> ignore<Task>

    interface ILoggerFactory with
        member this.AddProvider(provider: ILoggerProvider): unit =
            raise (NotImplementedException())
        member this.CreateLogger(categoryName: string): ILogger =
            MSTestLogger(logs.Writer, categoryName)
        member this.Dispose(): unit =
            logs.Writer.TryComplete()
            |> ignore<bool>

