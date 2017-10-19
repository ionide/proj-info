
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
        abstract ToolExe: string
        abstract StandardErrorEncoding : System.Text.Encoding
        abstract StandardOutputEncoding : System.Text.Encoding
        abstract GenerateFullPathToTool : unit -> string
        abstract LogToolCommand: string -> unit
        abstract ExecuteTool : pathToTool: string * responseFileCommands: string * commandLineCommands: string -> int
        abstract HostObject : obj

        abstract Log: TaskLoggingHelper

    type OutputAttribute () =
        inherit System.Attribute()

namespace Microsoft.Build.Utilities
    module Dummy =
        do ()

namespace Internal.Utilities

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
