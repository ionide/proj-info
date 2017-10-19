
namespace Microsoft.Build.Framework

    type MessageImportance =
        | Normal

    type TaskLoggingHelper () =
        member __.LogMessageFromText(message: string, importance: MessageImportance) =
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
        abstract LogToolCommand: string -> unit

        abstract member ExecuteTool : pathToTool: string * responseFileCommands: string * commandLineCommands: string -> int
        default x.ExecuteTool(pathToTool: string, responseFileCommands: string, commandLineCommands: string) =
            0

        abstract HostObject : obj
        default x.HostObject = Unchecked.defaultof<obj>

        member val ToolExe: string = "" with get, set
        member x.Log: TaskLoggingHelper = TaskLoggingHelper ()

    type OutputAttribute () =
        inherit System.Attribute()

namespace Internal.Utilities

    module FSBuild =
        module SR =
            
            let toolpathUnknown () = ""

