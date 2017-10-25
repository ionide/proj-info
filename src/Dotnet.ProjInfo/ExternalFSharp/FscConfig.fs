module Dotnet.ProjInfo.FakeMsbuildTasks

open System

// SRTP: how to set a property:
//
// let inline setReferences (e: ^a) x =
//     (^a: (member set_References: Microsoft.Build.Framework.ITaskItem array -> unit) (e, x))
//

type ITaskItemArray = Microsoft.Build.Framework.ITaskItem array

let inline getResponseFileFromTask props (fsc: ^a) =

    let m : Map<string, string> = props |> Map.ofList
    let prop k =
        match m |> Map.tryFind k with
        | Some x ->
            if String.IsNullOrWhiteSpace(x) then
                None
            else
                Some x
        | None -> failwithf "not found k=%s in '%0A" k m

    let bind f p =
        p |> prop |> Option.iter f
    let bindBool f p =
        let propBool k =
            let s = (prop k)
            try
                s
                |> Option.map (bool.Parse)
            with x ->
                failwithf "invalid k=%s s=%A" k s
        p |> propBool |> Option.iter f
    let bindArray f p =
        let propArray k =
            let x = prop k
            let f (s: string) = s.Split([| ';' |], StringSplitOptions.RemoveEmptyEntries)
            x |> Option.map f
        match p |> propArray with
        | None -> ()
        | Some l ->
            l
            |> Array.map Microsoft.Build.Utilities.TaskItem
            |> Array.map (fun x -> x :> Microsoft.Build.Framework.ITaskItem)
            |> f

    "BaseAddress"|> bind (fun prop -> (^a: (member set_BaseAddress: string -> unit) (fsc, prop)))
    "CodePage" |> bind (fun prop -> (^a: (member set_CodePage: string -> unit) (fsc, prop)))
    "DebugSymbols" |> bindBool (fun prop -> (^a: (member set_DebugSymbols: bool -> unit) (fsc, prop)))
    "DebugType" |> bind (fun prop -> (^a: (member set_DebugType: string -> unit) (fsc, prop)))
    "DefineConstants" |> bindArray (fun prop -> (^a: (member set_DefineConstants: ITaskItemArray -> unit) (fsc, prop)))
    "DelaySign" |> bindBool (fun prop -> (^a: (member set_DelaySign: bool -> unit) (fsc, prop)))
    "DisabledWarnings" |> bind (fun prop -> (^a: (member set_DisabledWarnings: string -> unit) (fsc, prop)))
    "DocumentationFile" |> bind (fun prop -> (^a: (member set_DocumentationFile: string -> unit) (fsc, prop)))
    "DotnetFscCompilerPath" |> bind (fun prop -> (^a: (member set_DotnetFscCompilerPath: string -> unit) (fsc, prop)))
    "EmbedAllSources" |> bindBool (fun prop -> (^a: (member set_EmbedAllSources: bool -> unit) (fsc, prop)))
    "Embed" |> bind (fun prop -> (^a: (member set_Embed: string -> unit) (fsc, prop)))
    "GenerateInterfaceFile" |> bind (fun prop -> (^a: (member set_GenerateInterfaceFile: string -> unit) (fsc, prop)))
    "HighEntropyVA" |> bindBool (fun prop -> (^a: (member set_HighEntropyVA: bool -> unit) (fsc, prop)))
    "KeyFile" |> bind (fun prop -> (^a: (member set_KeyFile: string -> unit) (fsc, prop)))
    "LCID" |> bind (fun prop -> (^a: (member set_LCID: string -> unit) (fsc, prop)))
    "NoFramework" |> bindBool (fun prop -> (^a: (member set_NoFramework: bool -> unit) (fsc, prop)))
    "Optimize" |> bindBool (fun prop -> (^a: (member set_Optimize: bool -> unit) (fsc, prop)))
    "OtherFlags" |> bind (fun prop -> (^a: (member set_OtherFlags: string -> unit) (fsc, prop)))
    "OutputAssembly" |> bind (fun prop -> (^a: (member set_OutputAssembly: string -> unit) (fsc, prop)))
    "PdbFile" |> bind (fun prop -> (^a: (member set_PdbFile: string -> unit) (fsc, prop)))
    "Platform" |> bind (fun prop -> (^a: (member set_Platform: string -> unit) (fsc, prop)))
    "Prefer32Bit" |>bindBool (fun prop -> (^a: (member set_Prefer32Bit: bool -> unit) (fsc, prop)))
    "PreferredUILang" |> bind (fun prop -> (^a: (member set_PreferredUILang: string -> unit) (fsc, prop)))
    "ProvideCommandLineArgs" |> bindBool (fun prop -> (^a: (member set_ProvideCommandLineArgs: bool -> unit) (fsc, prop)))
    "PublicSign" |> bindBool (fun prop -> (^a: (member set_PublicSign: bool -> unit) (fsc, prop)))
    "References" |> bindArray (fun prop -> (^a: (member set_References: ITaskItemArray -> unit) (fsc, prop)))
    "ReferencePath" |> bind (fun prop -> (^a: (member set_ReferencePath: string -> unit) (fsc, prop)))
    "Resources" |> bindArray (fun prop -> (^a: (member set_Resources: ITaskItemArray -> unit) (fsc, prop)))
    "SkipCompilerExecution" |> bindBool (fun prop -> (^a: (member set_SkipCompilerExecution: bool -> unit) (fsc, prop)))
    "SourceLink" |> bind (fun prop -> (^a: (member set_SourceLink: string -> unit) (fsc, prop)))
    "Sources" |> bindArray (fun prop -> (^a: (member set_Sources: ITaskItemArray -> unit) (fsc, prop)))
    "Tailcalls" |> bindBool (fun prop -> (^a: (member set_Tailcalls: bool -> unit) (fsc, prop)))
    "TargetType" |> bind (fun prop -> (^a: (member set_TargetType: string -> unit) (fsc, prop)))
    "TargetProfile" |> bind (fun prop -> (^a: (member set_TargetProfile: string -> unit) (fsc, prop)))
    "ToolExe" |> bind (fun prop -> (^a: (member set_ToolExe: string -> unit) (fsc, prop)))
    "ToolPath" |> bind (fun prop -> (^a: (member set_ToolPath: string -> unit) (fsc, prop)))
    "TreatWarningsAsErrors" |> bindBool (fun prop -> (^a: (member set_TreatWarningsAsErrors: bool -> unit) (fsc, prop)))
    "UseStandardResourceNames" |> bindBool (fun prop -> (^a: (member set_UseStandardResourceNames: bool -> unit) (fsc, prop)))
    "Utf8Output" |> bindBool (fun prop -> (^a: (member set_Utf8Output: bool -> unit) (fsc, prop)))
    "VersionFile" |> bind (fun prop -> (^a: (member set_VersionFile: string -> unit) (fsc, prop)))
    "VisualStudioStyleErrors" |> bindBool (fun prop -> (^a: (member set_VisualStudioStyleErrors: bool -> unit) (fsc, prop)))
    "WarningLevel" |> bind (fun prop -> (^a: (member set_WarningLevel: string -> unit) (fsc, prop)))
    "WarningsAsErrors" |> bind (fun prop -> (^a: (member set_WarningsAsErrors: string -> unit) (fsc, prop)))
    "Win32ManifestFile" |> bind (fun prop -> (^a: (member set_Win32ManifestFile: string -> unit) (fsc, prop)))
    "Win32ResourceFile" |> bind (fun prop -> (^a: (member set_Win32ResourceFile: string -> unit) (fsc, prop)))
    "SubsystemVersion" |> bind (fun prop -> (^a: (member set_SubsystemVersion: string -> unit) (fsc, prop)))

    //TODO force SkipCompilerExecution ?

    let responseFileText = (^a: (member GenerateResponseFileCommands: unit -> string) (fsc))

    responseFileText.Split([| Environment.NewLine |], StringSplitOptions.RemoveEmptyEntries)
    |> List.ofArray

let getFscTaskProperties () =

    let msFsharpTargetText = Resources.getResourceFileAsString "Microsoft.FSharp.Targets"

    let doc =
        try
            System.Xml.Linq.XDocument.Parse(msFsharpTargetText)
        with ex ->
            failwithf "Cannot parse xml embedded resource, %A" ex

    let xnNs name = System.Xml.Linq.XName.Get(name, doc.Root.GetDefaultNamespace().NamespaceName)
    let xn name = System.Xml.Linq.XName.Get(name)
    let attr name (x: Xml.Linq.XElement) = x.Attributes(xn name) |> Seq.map (fun a -> a.Value) |> Seq.tryHead
    let els name (x: Xml.Linq.XElement) = x.Elements(xnNs name)
    let el name (x: Xml.Linq.XElement) = x |> els name |> Seq.tryHead

    let targets =
        doc.Root
        |> els "Target"

    let coreCompile =
        targets
        |> Seq.tryFind (attr "Name" >> Option.exists ((=) "CoreCompile"))

    match coreCompile with
    | None ->
        failwithf "CoreCompile target not found from embedded resource"
    | Some t ->
        match t |> el "Fsc" with
        | None ->
            failwithf "FscTask not found from embedded resource"
        | Some fscTask ->
            fscTask.Attributes()
            |> Seq.filter (fun a -> a.Name.LocalName <> "Condition")
            |> Seq.map (fun a -> a.Name.LocalName, a.Value)
            |> List.ofSeq
