
namespace Microsoft.Build.Framework

    type MessageImportance =
        | Normal

    type TaskLoggingHelper () =
        member __.LogMessageFromText(_message: string, _importance: MessageImportance) =
            ()
        member __.LogError(_message: string, _: string array) =
            ()

    [<AbstractClass>]
    type ToolTask () =

        member val YieldDuringToolExecution: bool = false with get, set

        abstract member GenerateCommandLineCommands : unit -> string

        abstract member GenerateResponseFileCommands : unit -> string

        abstract ToolName : string 
        abstract StandardErrorEncoding : System.Text.Encoding
        default x.StandardErrorEncoding = System.Text.Encoding.UTF8

        abstract StandardOutputEncoding : System.Text.Encoding
        default x.StandardOutputEncoding = System.Text.Encoding.UTF8

        abstract GenerateFullPathToTool : unit -> string

        abstract member ExecuteTool : pathToTool: string * responseFileCommands: string * commandLineCommands: string -> int
        default x.ExecuteTool(_pathToTool: string, _responseFileCommands: string, _commandLineCommands: string) =
            0

        abstract HostObject : obj
        default x.HostObject = Unchecked.defaultof<obj>

        member val ToolExe: string = "" with get, set
        member x.Log: TaskLoggingHelper = TaskLoggingHelper ()

    type OutputAttribute () =
        inherit System.Attribute()

namespace Microsoft.Build.Exceptions

    exception BuildAbortedException

namespace Internal.Utilities

    module FSBuild =
        module SR =
            
            let toolpathUnknown () = ""


    module FSharpEnvironment =
        let BinFolderOfDefaultFSharpCompiler (_: string option) = Some "dummy path"
