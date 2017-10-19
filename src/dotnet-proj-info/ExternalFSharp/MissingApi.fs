
namespace Microsoft.Build.Framework

    type ITaskItem =
        abstract ItemSpec: string with get, set
        abstract member GetMetadata : string -> string
    
    type TaskItem(arg: string) =

        interface ITaskItem with
            member val ItemSpec: string = "" with get, set

            member __.GetMetadata(key: string) =
                ""

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

        member x.ToolExe: string = ""
        member x.Log: TaskLoggingHelper = TaskLoggingHelper ()

    type OutputAttribute () =
        inherit System.Attribute()

namespace Microsoft.Build.Utilities

    open Microsoft.Build.Framework

    type CommandLineBuilder () =
        
        member x.AppendTextUnquoted (s:string) =
            ()

        
        member x.AppendSwitch (s:string) =
            ()

        member x.AppendSwitchUnquotedIfNotNull (switch: string, value: string, ?sep: string) =
            ()

        member x.AppendSwitchUnquotedIfNotNull (switch: string, values: string array, ?sep: string) =
            ()

        member x.AppendSwitchIfNotNull (switch: string, value: string, ?sep: string) =
            ()

        member x.AppendSwitchIfNotNull (switch: string, value: string array, ?sep: string) =
            ()

        member x.AppendFileNamesIfNotNull (filenames: ITaskItem array, sep: string) =
            ()

namespace Internal.Utilities

    module FSBuild =
        module SR =
            
            let toolpathUnknown () = ""

