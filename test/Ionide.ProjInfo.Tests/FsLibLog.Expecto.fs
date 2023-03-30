namespace FsLibLog.Providers.Expecto

open System
open Ionide.ProjInfo.Logging

module EMsg = Expecto.Logging.Message
type ELL = Expecto.Logging.LogLevel

module internal Helpers =
    let addExnOpt exOpt msg =
        match exOpt with
        | None -> msg
        | Some ex ->
            msg
            |> EMsg.addExn ex

    let addValues (items: obj[]) msg =
        (msg,
         items
         |> Seq.mapi (fun i item -> i, item))
        ||> Seq.fold (fun msg (i, item) ->
            msg
            |> EMsg.setField (string i) item
        )

    let getLogLevel: LogLevel -> Expecto.Logging.LogLevel =
        function
        | LogLevel.Debug -> ELL.Debug
        | LogLevel.Error -> ELL.Error
        | LogLevel.Fatal -> ELL.Fatal
        | LogLevel.Info -> ELL.Info
        | LogLevel.Trace -> ELL.Verbose
        | LogLevel.Warn -> ELL.Warn
        | _ -> ELL.Warn

open Helpers

// Naive implementation, not that important, just need logging to actually work
type ExpectoLogProvider() =
    let propertyStack = System.Collections.Generic.Stack<string * obj>()


    let addProp key value =
        propertyStack.Push(key, value)

        { new IDisposable with
            member __.Dispose() =
                propertyStack.Pop()
                |> ignore
        }

    interface ILogProvider with
        override __.GetLogger(name: string) : Logger =
            let logger = Expecto.Logging.Log.create name

            fun ll mt exnOpt values ->
                match mt with
                | Some f ->
                    let ll = getLogLevel ll

                    logger.log
                        ll
                        (fun ll ->
                            let message = f ()
                            let mutable msg = Expecto.Logging.Message.eventX message ll

                            for (propertyName, propertyValue) in (Seq.rev propertyStack) do
                                msg <- Expecto.Logging.Message.setField propertyName propertyValue msg

                            match exnOpt with
                            | None -> msg
                            | Some ex ->
                                msg
                                |> Expecto.Logging.Message.addExn ex
                            |> addValues values
                        )
                    |> Async.RunSynchronously

                    true
                | None -> false

        override __.OpenMappedContext (key: string) (value: obj) (b: bool) = addProp key value
        override __.OpenNestedContext name = addProp "NDC" name

module ExpectoLogProvider =
    let create () = ExpectoLogProvider() :> ILogProvider
