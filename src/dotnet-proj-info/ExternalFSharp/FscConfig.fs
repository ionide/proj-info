module Dotnet.ProjInfo.FakeMsbuildTasks

open System

let config props =

    let fsc = Microsoft.FSharp.Build.Fsc()
    
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
            |> Array.map Microsoft.Build.Framework.TaskItem
            |> Array.map (fun x -> x :> Microsoft.Build.Framework.ITaskItem)
            |> f

    bind (fun prop -> fsc.BaseAddress <- prop) "BaseAddress"
    bind (fun prop -> fsc.CodePage <- prop) "CodePage"
    bindBool (fun prop -> fsc.DebugSymbols <- prop) "DebugSymbols"
    bind (fun prop -> fsc.DebugType <- prop) "DebugType"
    bindArray (fun prop -> fsc.DefineConstants <- prop) "DefineConstants"
    bindBool (fun prop -> fsc.DelaySign <- prop) "DelaySign"
    bind (fun prop -> fsc.DisabledWarnings <- prop) "DisabledWarnings"
    bind (fun prop -> fsc.DocumentationFile <- prop) "DocumentationFile"
    bind (fun prop -> fsc.DotnetFscCompilerPath <- prop) "DotnetFscCompilerPath"
    bindBool (fun prop -> fsc.EmbedAllSources <- prop) "EmbedAllSources"
    bind (fun prop -> fsc.Embed <- prop) "Embed"
    bind (fun prop -> fsc.GenerateInterfaceFile <- prop) "GenerateInterfaceFile"
    bindBool (fun prop -> fsc.HighEntropyVA <- prop) "HighEntropyVA"
    bind (fun prop -> fsc.KeyFile <- prop) "KeyFile"
    bind (fun prop -> fsc.LCID <- prop) "LCID"
    //bindBool (fun prop -> fsc.NoFramework <- prop) "true"
    bindBool (fun prop -> fsc.Optimize <- prop) "Optimize"
    bind (fun prop -> fsc.OtherFlags <- prop) "OtherFlags"
    bind (fun prop -> fsc.OutputAssembly <- prop) "OutputAssembly"
    bind (fun prop -> fsc.PdbFile <- prop) "PdbFile"
    bind (fun prop -> fsc.Platform <- prop) "Platform"
    bindBool (fun prop -> fsc.Prefer32Bit <- prop) "Prefer32Bit"
    bind (fun prop -> fsc.PreferredUILang <- prop) "PreferredUILang"
    bindBool (fun prop -> fsc.ProvideCommandLineArgs <- prop) "ProvideCommandLineArgs"
    bindBool (fun prop -> fsc.PublicSign <- prop) "PublicSign"
    bindArray (fun prop -> fsc.References <- prop) "ReferencePath"
    bind (fun prop -> fsc.ReferencePath <- prop) "ReferencePath"
    bindArray (fun prop -> fsc.Resources <- prop) "Resources"
    bindBool (fun prop -> fsc.SkipCompilerExecution <- prop) "SkipCompilerExecution"
    bind (fun prop -> fsc.SourceLink <- prop) "SourceLink"
    bindArray (fun prop -> fsc.Sources <- prop) "Sources"
    bindBool (fun prop -> fsc.Tailcalls <- prop) "Tailcalls"
    bind (fun prop -> fsc.TargetType <- prop) "TargetType"
    bind (fun prop -> fsc.TargetProfile <- prop) "TargetProfile"
    bind (fun prop -> fsc.ToolExe <- prop) "ToolExe"
    bind (fun prop -> fsc.ToolPath <- prop) "ToolPath"
    bindBool (fun prop -> fsc.TreatWarningsAsErrors <- prop) "TreatWarningsAsErrors"
    bindBool (fun prop -> fsc.UseStandardResourceNames <- prop) "UseStandardResourceNames"
    bindBool (fun prop -> fsc.Utf8Output <- prop) "Utf8Output"
    bind (fun prop -> fsc.VersionFile <- prop) "VersionFile"
    bindBool (fun prop -> fsc.VisualStudioStyleErrors <- prop) "VisualStudioStyleErrors"
    bind (fun prop -> fsc.WarningLevel <- prop) "WarningLevel"
    bind (fun prop -> fsc.WarningsAsErrors <- prop) "WarningsAsErrors"
    bind (fun prop -> fsc.Win32ManifestFile <- prop) "Win32ManifestFile"
    bind (fun prop -> fsc.Win32ResourceFile <- prop) "Win32ResourceFile"
    bind (fun prop -> fsc.SubsystemVersion <- prop) "SubsystemVersion"

    let responseFileText = fsc.InternalGenerateResponseFileCommands()

    responseFileText.Split([| Environment.NewLine |], StringSplitOptions.RemoveEmptyEntries)
    |> List.ofArray

