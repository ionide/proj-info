open System
open Argu

type CLIArguments =
    | [<AltCommandLine("-v")>] Verbose
    | [<CliPrefix(CliPrefix.None)>] Prop of ParseResults<PropCLIArguments>
    | [<CliPrefix(CliPrefix.None)>] Fsc_Args of ParseResults<FscArgsCLIArguments>
    | [<CliPrefix(CliPrefix.None)>] Csc_Args of ParseResults<CscArgsCLIArguments>
    | [<CliPrefix(CliPrefix.None)>] P2p of ParseResults<P2pCLIArguments>
    | [<CliPrefix(CliPrefix.None)>] Item of ParseResults<ItemCLIArguments>
    | [<CliPrefix(CliPrefix.None)>] Net_Fw of ParseResults<NetFwCLIArguments>
    | [<CliPrefix(CliPrefix.None)>] Net_Fw_Ref of ParseResults<NetFwRefCLIArguments>
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Verbose -> "verbose log"
            | Prop _ -> "get properties"
            | Fsc_Args _ -> "get fsc arguments"
            | Csc_Args _ -> "get csc arguments"
            | P2p _ -> "get project references"
            | Item _ -> "get items"
            | Net_Fw _ -> "list the installed .NET Frameworks"
            | Net_Fw_Ref _ -> "get the reference path of given .NET Framework assembly"
and MSBuildHostPicker =
    | Auto = 1
    | MSBuild  = 2
    | DotnetMSBuild = 3
and PropCLIArguments =
    | [<MainCommand; Unique>] Project of string
    | [<AltCommandLine("-get")>] GetProperty of string
    | [<AltCommandLine("-p")>] Property of string list
    | [<AltCommandLine("-f")>] Framework of string
    | [<AltCommandLine("-r")>] Runtime of string
    | [<AltCommandLine("-c")>] Configuration of string
    | MSBuild of string
    | DotnetCli of string
    | MSBuild_Host of MSBuildHostPicker
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Project _ -> "the MSBuild project file"
            | Framework _ -> "target framework, the TargetFramework msbuild property"
            | Runtime _ -> "target runtime, the RuntimeIdentifier msbuild property"
            | Configuration _ -> "configuration to use (like Debug), the Configuration msbuild property"
            | GetProperty _ -> "msbuild property to get (allow multiple)"
            | Property _ -> "msbuild property to use (allow multiple)"
            | MSBuild _ -> """MSBuild path (default "msbuild")"""
            | DotnetCli _ -> """Dotnet CLI path (default "dotnet")"""
            | MSBuild_Host _ -> "the Msbuild host, if auto then oldsdk=MSBuild dotnetSdk=DotnetCLI"
and ItemCLIArguments =
    | [<MainCommand; Unique>] Project of string
    | [<AltCommandLine("-get")>] GetItem of string
    | [<AltCommandLine("-p")>] Property of string list
    | [<AltCommandLine("-f")>] Framework of string
    | [<AltCommandLine("-r")>] Runtime of string
    | [<AltCommandLine("-c")>] Configuration of string
    | [<AltCommandLine("-d")>] Depends_On of string
    | MSBuild of string
    | DotnetCli of string
    | MSBuild_Host of MSBuildHostPicker
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Project _ -> "the MSBuild project file"
            | Framework _ -> "target framework, the TargetFramework msbuild property"
            | Runtime _ -> "target runtime, the RuntimeIdentifier msbuild property"
            | Configuration _ -> "configuration to use (like Debug), the Configuration msbuild property"
            | GetItem _ -> "msbuild item to get (allow multiple)"
            | Property _ -> "msbuild property to use (allow multiple)"
            | MSBuild _ -> """MSBuild path (default "msbuild")"""
            | DotnetCli _ -> """Dotnet CLI path (default "dotnet")"""
            | MSBuild_Host _ -> "the Msbuild host, if auto then oldsdk=MSBuild dotnetSdk=DotnetCLI"
            | Depends_On _ -> "the Msbuild host, if auto then oldsdk=MSBuild dotnetSdk=DotnetCLI"
and FscArgsCLIArguments =
    | [<MainCommand; Unique>] Project of string
    | [<AltCommandLine("-p")>] Property of string list
    | [<AltCommandLine("-f")>] Framework of string
    | [<AltCommandLine("-r")>] Runtime of string
    | [<AltCommandLine("-c")>] Configuration of string
    | MSBuild of string
    | DotnetCli of string
    | MSBuild_Host of MSBuildHostPicker
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Project _ -> "the MSBuild project file"
            | Framework _ -> "target framework, the TargetFramework msbuild property"
            | Runtime _ -> "target runtime, the RuntimeIdentifier msbuild property"
            | Configuration _ -> "configuration to use (like Debug), the Configuration msbuild property"
            | Property _ -> "msbuild property to use (allow multiple)"
            | MSBuild _ -> """MSBuild path (default "msbuild")"""
            | DotnetCli _ -> """Dotnet CLI path (default "dotnet")"""
            | MSBuild_Host _ -> "the Msbuild host, if auto then oldsdk=MSBuild dotnetSdk=DotnetCLI"
and CscArgsCLIArguments =
    | [<MainCommand; Unique>] Project of string
    | [<AltCommandLine("-p")>] Property of string list
    | [<AltCommandLine("-f")>] Framework of string
    | [<AltCommandLine("-r")>] Runtime of string
    | [<AltCommandLine("-c")>] Configuration of string
    | MSBuild of string
    | DotnetCli of string
    | MSBuild_Host of MSBuildHostPicker
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Project _ -> "the MSBuild project file"
            | Framework _ -> "target framework, the TargetFramework msbuild property"
            | Runtime _ -> "target runtime, the RuntimeIdentifier msbuild property"
            | Configuration _ -> "configuration to use (like Debug), the Configuration msbuild property"
            | Property _ -> "msbuild property to use (allow multiple)"
            | MSBuild _ -> """MSBuild path (default "msbuild")"""
            | DotnetCli _ -> """Dotnet CLI path (default "dotnet")"""
            | MSBuild_Host _ -> "the Msbuild host, if auto then oldsdk=MSBuild dotnetSdk=DotnetCLI"
and P2pCLIArguments =
    | [<MainCommand; Unique>] Project of string
    | [<AltCommandLine("-p")>] Property of string list
    | [<AltCommandLine("-f")>] Framework of string
    | [<AltCommandLine("-r")>] Runtime of string
    | [<AltCommandLine("-c")>] Configuration of string
    | MSBuild of string
    | DotnetCli of string
    | MSBuild_Host of MSBuildHostPicker
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Project _ -> "the MSBuild project file"
            | Framework _ -> "target framework, the TargetFramework msbuild property"
            | Runtime _ -> "target runtime, the RuntimeIdentifier msbuild property"
            | Configuration _ -> "configuration to use (like Debug), the Configuration msbuild property"
            | Property _ -> "msbuild property to use (allow multiple)"
            | MSBuild _ -> """MSBuild path (default "msbuild")"""
            | DotnetCli _ -> """Dotnet CLI path (default "dotnet")"""
            | MSBuild_Host _ -> "the Msbuild host, if auto then oldsdk=MSBuild dotnetSdk=DotnetCLI"
and NetFwCLIArguments =
    | MSBuild of string
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | MSBuild _ -> """MSBuild path (default "msbuild")"""
and NetFwRefCLIArguments =
    | [<MainCommand>] Assembly of string
    | MSBuild of string
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Assembly _ -> "the name of the assembly"
            | MSBuild _ -> """MSBuild path (default "msbuild")"""

open Dotnet.ProjInfo.Inspect

type Errors =
    | InvalidArgs of Argu.ArguParseException
    | InvalidArgsState of string
    | ProjectFileNotFound of string
    | GenericError of string
    | RaisedException of System.Exception * string
    | ExecutionError of GetProjectInfoErrors<ShellCommandResult>
and ShellCommandResult = ShellCommandResult of workingDir: string * exePath: string * args: string

let parseArgsCommandLine argv =
    try
        let parser = ArgumentParser.Create<CLIArguments>(programName = "dotnet-proj")
        let results = parser.Parse argv
        Ok results
    with
        | :? ArguParseException as ex ->
            Error (InvalidArgs ex)
        | _ ->
            reraise ()

open Railway
open System.IO

let runCmd log workingDir exePath args =
    log (sprintf "running '%s %s'" exePath (args |> String.concat " "))

    let logOut = System.Collections.Concurrent.ConcurrentQueue<string>()
    let logErr = System.Collections.Concurrent.ConcurrentQueue<string>()

    let runProcess (workingDir: string) (exePath: string) (args: string) =
        let psi = System.Diagnostics.ProcessStartInfo()
        psi.FileName <- exePath
        psi.WorkingDirectory <- workingDir
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.Arguments <- args
        psi.CreateNoWindow <- true
        psi.UseShellExecute <- false

        //Some env var like `MSBUILD_EXE_PATH` override the msbuild used.
        //The dotnet cli (`dotnet`) set these when calling child processes, and
        //is wrong because these override some properties of the called msbuild
        let msbuildEnvVars =
            psi.Environment.Keys
            |> Seq.filter (fun s -> s.StartsWith("msbuild", StringComparison.OrdinalIgnoreCase))
            |> Seq.toList
        for msbuildEnvVar in msbuildEnvVars do
            psi.Environment.Remove(msbuildEnvVar) |> ignore


        use p = new System.Diagnostics.Process()
        p.StartInfo <- psi

        p.OutputDataReceived.Add(fun ea -> logOut.Enqueue (ea.Data))

        p.ErrorDataReceived.Add(fun ea -> logErr.Enqueue (ea.Data))

        p.Start() |> ignore
        p.BeginOutputReadLine()
        p.BeginErrorReadLine()
        p.WaitForExit()

        let exitCode = p.ExitCode

        exitCode, (workingDir, exePath, args)

    let exitCode, result = runProcess workingDir exePath (args |> String.concat " ")

    log "output:"
    logOut.ToArray()
    |> Array.iter log

    log "error:"
    logErr.ToArray()
    |> Array.iter log

    exitCode, (ShellCommandResult result)

let validateProj log projOpt = attempt {
    let scanDirForProj workDir =
        match Directory.GetFiles(workDir, "*.*proj") |> List.ofArray with
        | [] ->
            Error (InvalidArgsState "no .*proj project found in current directory, use --project argument to specify path")
        | [x] -> Ok x
        | _xs ->
            Error (InvalidArgsState "multiple .*proj found in current directory, use --project argument to specify path")

    let! proj =
        match projOpt with
        | Some p -> Ok p
        | None ->
            //scan current directory
            let workDir = Directory.GetCurrentDirectory()
            scanDirForProj workDir

    let projPath = proj |> Path.GetFullPath

    log (sprintf "resolved project path '%s'" projPath)

    do! if not (File.Exists projPath)
        then Error (ProjectFileNotFound projPath)
        else Ok ()

    return projPath
    }

let analizeProj projPath = attempt {

    let! (isDotnetSdk, pi) =
        match projPath with
        | ProjectRecognizer.DotnetSdk pi ->
            Ok (true, pi)
        | ProjectRecognizer.OldSdk pi ->
#if NETCOREAPP1_0
            Errors.GenericError "unsupported project format on .net core 1.0, use at least .net core 2.0"
            |> Result.Error
#else
            Ok (false, pi)
#endif
        | ProjectRecognizer.Unsupported ->
            Errors.GenericError "unsupported project format"
            |> Result.Error

    return isDotnetSdk, pi, getProjectInfo
    }

let propMain log (results: ParseResults<PropCLIArguments>) = attempt {

    let! projPath =
        results.TryGetResult <@ PropCLIArguments.Project @>
        |> validateProj log

    let! (isDotnetSdk, _, getProjectInfoBySdk) = analizeProj projPath

    let globalArgs =
        [ results.TryGetResult <@ PropCLIArguments.Framework @>, if isDotnetSdk then "TargetFramework" else "TargetFrameworkVersion"
          results.TryGetResult <@ PropCLIArguments.Runtime @>, "RuntimeIdentifier"
          results.TryGetResult <@ PropCLIArguments.Configuration @>, "Configuration" ]
        |> List.choose (fun (a,p) -> a |> Option.map (fun x -> (p,x)))
        |> List.map (MSBuild.MSbuildCli.Property)

    let msbuildPath = results.GetResult(<@ PropCLIArguments.MSBuild @>, defaultValue = "msbuild")
    let dotnetPath = results.GetResult(<@ PropCLIArguments.DotnetCli @>, defaultValue = "dotnet")
    let dotnetHostPicker = results.GetResult(<@ PropCLIArguments.MSBuild_Host @>, defaultValue = MSBuildHostPicker.Auto)

    let props = results.GetResults <@ PropCLIArguments.GetProperty @>

    let cmd () = getProperties props

    let rec msbuildHost host =
        match host with
        | MSBuildHostPicker.MSBuild ->
            MSBuildExePath.Path msbuildPath
        | MSBuildHostPicker.DotnetMSBuild ->
            MSBuildExePath.DotnetMsbuild dotnetPath
        | MSBuildHostPicker.Auto ->
            if isDotnetSdk then
                msbuildHost MSBuildHostPicker.DotnetMSBuild
            else
                msbuildHost MSBuildHostPicker.MSBuild
        | x ->
            failwithf "Unexpected msbuild host '%A'" x

    return projPath, getProjectInfoBySdk, cmd, (msbuildHost dotnetHostPicker), globalArgs
    }

let itemMain log (results: ParseResults<ItemCLIArguments>) = attempt {

    let! projPath =
        results.TryGetResult <@ ItemCLIArguments.Project @>
        |> validateProj log

    let! (isDotnetSdk, _, getProjectInfoBySdk) = analizeProj projPath

    let globalArgs =
        [ results.TryGetResult <@ ItemCLIArguments.Framework @>, if isDotnetSdk then "TargetFramework" else "TargetFrameworkVersion"
          results.TryGetResult <@ ItemCLIArguments.Runtime @>, "RuntimeIdentifier"
          results.TryGetResult <@ ItemCLIArguments.Configuration @>, "Configuration" ]
        |> List.choose (fun (a,p) -> a |> Option.map (fun x -> (p,x)))
        |> List.map (MSBuild.MSbuildCli.Property)

    let msbuildPath = results.GetResult(<@ ItemCLIArguments.MSBuild @>, defaultValue = "msbuild")
    let dotnetPath = results.GetResult(<@ ItemCLIArguments.DotnetCli @>, defaultValue = "dotnet")
    let dotnetHostPicker = results.GetResult(<@ ItemCLIArguments.MSBuild_Host @>, defaultValue = MSBuildHostPicker.Auto)

    let parseItemPath (path: string) =
        match path.Split('.') |> List.ofArray with
        | [p] -> p, GetItemsModifier.Identity
        | [p; m] ->
            let modifier =
                match m.ToLower() with
                | "identity" -> GetItemsModifier.Identity
                | "fullpath" -> GetItemsModifier.FullPath
                | _ -> GetItemsModifier.Custom m
            p, modifier
        | _ -> failwithf "Unexpected item path '%s'. Expected format is 'ItemName' or 'ItemName.Metadata' (like Compile.Identity or Compile.FullPath)" path

    let items =
        results.GetResults <@ ItemCLIArguments.GetItem @>
        |> List.map parseItemPath

    let dependsOn = results.GetResults <@ ItemCLIArguments.Depends_On @>

    let cmd () = getItems items dependsOn

    let rec msbuildHost host =
        match host with
        | MSBuildHostPicker.MSBuild ->
            MSBuildExePath.Path msbuildPath
        | MSBuildHostPicker.DotnetMSBuild ->
            MSBuildExePath.DotnetMsbuild dotnetPath
        | MSBuildHostPicker.Auto ->
            if isDotnetSdk then
                msbuildHost MSBuildHostPicker.DotnetMSBuild
            else
                msbuildHost MSBuildHostPicker.MSBuild
        | x ->
            failwithf "Unexpected msbuild host '%A'" x

    return projPath, getProjectInfoBySdk, cmd, (msbuildHost dotnetHostPicker), globalArgs
    }

let fscArgsMain log (results: ParseResults<FscArgsCLIArguments>) = attempt {

    let! projPath =
        results.TryGetResult <@ FscArgsCLIArguments.Project @>
        |> validateProj log

    let! (isDotnetSdk, pi, getProjectInfoBySdk) = analizeProj projPath

    let! getCompilerArgsBySdk =
        match isDotnetSdk, pi.Language with
        | true, ProjectRecognizer.ProjectLanguage.FSharp ->
            Ok getFscArgs
        | false, ProjectRecognizer.ProjectLanguage.FSharp ->
            let asFscArgs props =
                let fsc = Microsoft.FSharp.Build.Fsc()
                Dotnet.ProjInfo.FakeMsbuildTasks.getResponseFileFromTask props fsc
            Ok (getFscArgsOldSdk (asFscArgs >> Ok))
        | _, ProjectRecognizer.ProjectLanguage.CSharp ->
            Errors.GenericError (sprintf "fsc args not supported on .csproj, expected an .fsproj" )
            |> Result.Error
        | _, ProjectRecognizer.ProjectLanguage.Unknown ext ->
            Errors.GenericError (sprintf "compiler args not supported on project with extension %s, expected .fsproj" ext)
            |> Result.Error

    let globalArgs =
        [ results.TryGetResult <@ FscArgsCLIArguments.Framework @>, if isDotnetSdk then "TargetFramework" else "TargetFrameworkVersion"
          results.TryGetResult <@ FscArgsCLIArguments.Runtime @>, "RuntimeIdentifier"
          results.TryGetResult <@ FscArgsCLIArguments.Configuration @>, "Configuration" ]
        |> List.choose (fun (a,p) -> a |> Option.map (fun x -> (p,x)))
        |> List.map (MSBuild.MSbuildCli.Property)

    let msbuildPath = results.GetResult(<@ FscArgsCLIArguments.MSBuild @>, defaultValue = "msbuild")
    let dotnetPath = results.GetResult(<@ FscArgsCLIArguments.DotnetCli @>, defaultValue = "dotnet")
    let dotnetHostPicker = results.GetResult(<@ FscArgsCLIArguments.MSBuild_Host @>, defaultValue = MSBuildHostPicker.Auto)

    let cmd = getCompilerArgsBySdk

    let rec msbuildHost host =
        match host with
        | MSBuildHostPicker.MSBuild ->
            MSBuildExePath.Path msbuildPath
        | MSBuildHostPicker.DotnetMSBuild ->
            MSBuildExePath.DotnetMsbuild dotnetPath
        | MSBuildHostPicker.Auto ->
            if isDotnetSdk then
                msbuildHost MSBuildHostPicker.DotnetMSBuild
            else
                msbuildHost MSBuildHostPicker.MSBuild
        | x ->
            failwithf "Unexpected msbuild host '%A'" x

    return projPath, getProjectInfoBySdk, cmd, (msbuildHost dotnetHostPicker), globalArgs
    }

let cscArgsMain log (results: ParseResults<CscArgsCLIArguments>) = attempt {

    let! projPath =
        results.TryGetResult <@ CscArgsCLIArguments.Project @>
        |> validateProj log

    let! (isDotnetSdk, pi, getProjectInfoBySdk) = analizeProj projPath

    let! getCompilerArgsBySdk =
        match isDotnetSdk, pi.Language with
        | true, ProjectRecognizer.ProjectLanguage.CSharp ->
            Ok getCscArgs
        | false, ProjectRecognizer.ProjectLanguage.CSharp ->
            Errors.GenericError "csc args not supported on old sdk"
            |> Result.Error
        | _, ProjectRecognizer.ProjectLanguage.FSharp ->
            Errors.GenericError (sprintf "csc args not supported on .fsproj, expected an .csproj" )
            |> Result.Error
        | _, ProjectRecognizer.ProjectLanguage.Unknown ext ->
            Errors.GenericError (sprintf "compiler args not supported on project with extension %s" ext)
            |> Result.Error

    let globalArgs =
        [ results.TryGetResult <@ CscArgsCLIArguments.Framework @>, if isDotnetSdk then "TargetFramework" else "TargetFrameworkVersion"
          results.TryGetResult <@ CscArgsCLIArguments.Runtime @>, "RuntimeIdentifier"
          results.TryGetResult <@ CscArgsCLIArguments.Configuration @>, "Configuration" ]
        |> List.choose (fun (a,p) -> a |> Option.map (fun x -> (p,x)))
        |> List.map (MSBuild.MSbuildCli.Property)

    let msbuildPath = results.GetResult(<@ CscArgsCLIArguments.MSBuild @>, defaultValue = "msbuild")
    let dotnetPath = results.GetResult(<@ CscArgsCLIArguments.DotnetCli @>, defaultValue = "dotnet")
    let dotnetHostPicker = results.GetResult(<@ CscArgsCLIArguments.MSBuild_Host @>, defaultValue = MSBuildHostPicker.Auto)

    let cmd = getCompilerArgsBySdk

    let rec msbuildHost host =
        match host with
        | MSBuildHostPicker.MSBuild ->
            MSBuildExePath.Path msbuildPath
        | MSBuildHostPicker.DotnetMSBuild ->
            MSBuildExePath.DotnetMsbuild dotnetPath
        | MSBuildHostPicker.Auto ->
            if isDotnetSdk then
                msbuildHost MSBuildHostPicker.DotnetMSBuild
            else
                msbuildHost MSBuildHostPicker.MSBuild
        | x ->
            failwithf "Unexpected msbuild host '%A'" x

    return projPath, getProjectInfoBySdk, cmd, (msbuildHost dotnetHostPicker), globalArgs
    }

let p2pMain log (results: ParseResults<P2pCLIArguments>) = attempt {

    let! projPath =
        results.TryGetResult <@ P2pCLIArguments.Project @>
        |> validateProj log

    let! (isDotnetSdk, pi, getProjectInfoBySdk) = analizeProj projPath

    let getCompilerArgsBySdk () =
        match isDotnetSdk, pi.Language with
        | true, ProjectRecognizer.ProjectLanguage.FSharp ->
            Ok getFscArgs
        | true, ProjectRecognizer.ProjectLanguage.CSharp ->
            Ok getCscArgs
        | false, ProjectRecognizer.ProjectLanguage.FSharp ->
            let asFscArgs props =
                let fsc = Microsoft.FSharp.Build.Fsc()
                Dotnet.ProjInfo.FakeMsbuildTasks.getResponseFileFromTask props fsc
            Ok (getFscArgsOldSdk (asFscArgs >> Ok))
        | false, ProjectRecognizer.ProjectLanguage.CSharp ->
            Errors.GenericError "csc args not supported on old sdk"
            |> Result.Error
        | _, ProjectRecognizer.ProjectLanguage.Unknown ext ->
            Errors.GenericError (sprintf "compiler args not supported on project with extension %s" ext)
            |> Result.Error

    let globalArgs =
        [ results.TryGetResult <@ P2pCLIArguments.Framework @>, if isDotnetSdk then "TargetFramework" else "TargetFrameworkVersion"
          results.TryGetResult <@ P2pCLIArguments.Runtime @>, "RuntimeIdentifier"
          results.TryGetResult <@ P2pCLIArguments.Configuration @>, "Configuration" ]
        |> List.choose (fun (a,p) -> a |> Option.map (fun x -> (p,x)))
        |> List.map (MSBuild.MSbuildCli.Property)

    let msbuildPath = results.GetResult(<@ P2pCLIArguments.MSBuild @>, defaultValue = "msbuild")
    let dotnetPath = results.GetResult(<@ P2pCLIArguments.DotnetCli @>, defaultValue = "dotnet")
    let dotnetHostPicker = results.GetResult(<@ P2pCLIArguments.MSBuild_Host @>, defaultValue = MSBuildHostPicker.Auto)

    let cmd = getP2PRefs

    let rec msbuildHost host =
        match host with
        | MSBuildHostPicker.MSBuild ->
            MSBuildExePath.Path msbuildPath
        | MSBuildHostPicker.DotnetMSBuild ->
            MSBuildExePath.DotnetMsbuild dotnetPath
        | MSBuildHostPicker.Auto ->
            if isDotnetSdk then
                msbuildHost MSBuildHostPicker.DotnetMSBuild
            else
                msbuildHost MSBuildHostPicker.MSBuild
        | x ->
            failwithf "Unexpected msbuild host '%A'" x

    return projPath, getProjectInfoBySdk, cmd, (msbuildHost dotnetHostPicker), globalArgs
    }

let netFwMain log (results: ParseResults<NetFwCLIArguments>) = attempt {

    let projPath =
        //create the proj file
        Dotnet.ProjInfo.NETFrameworkInfoFromMSBuild.createEnvInfoProj ()
        |> Path.GetFullPath

    let msbuildPath = results.GetResult(<@ NetFwCLIArguments.MSBuild @>, defaultValue = "msbuild")

    let cmd = Dotnet.ProjInfo.NETFrameworkInfoFromMSBuild.installedNETFrameworks

    let msbuildHost = MSBuildExePath.Path msbuildPath

    return projPath, getProjectInfo, cmd, msbuildHost, []
    }

let netFwRefMain log (results: ParseResults<NetFwRefCLIArguments>) = attempt {

    let! props =
        match results.GetResults <@ Assembly @> with
        | [] -> Error (InvalidArgsState "multiple .*proj found in current directory, use --project argument to specify path")
        | props -> Ok props

    let projPath =
        //create the proj file
        Dotnet.ProjInfo.NETFrameworkInfoFromMSBuild.createEnvInfoProj ()
        |> Path.GetFullPath

    let msbuildPath = results.GetResult(<@ NetFwRefCLIArguments.MSBuild @>, defaultValue = "msbuild")

    let cmd () = Dotnet.ProjInfo.NETFrameworkInfoFromMSBuild.getReferencePaths props

    let msbuildHost = MSBuildExePath.Path msbuildPath

    return projPath, getProjectInfo, cmd, msbuildHost, []
    }

let realMain argv = attempt {

    let! results = parseArgsCommandLine argv

    let log =
        match results.TryGetResult <@ Verbose @> with
        | Some _ -> printfn "%s"
        | None -> ignore

    let! (projPath, getProjectInfoBySdk, cmd, msbuildHost, globalArgs) =
        match results.TryGetSubCommand () with
        | Some (Prop subCmd) ->
            propMain log subCmd
        | Some (Item subCmd) ->
            itemMain log subCmd
        | Some (Fsc_Args subCmd) ->
            fscArgsMain log subCmd
        | Some (Csc_Args subCmd) ->
            cscArgsMain log subCmd
        | Some (P2p subCmd) ->
            p2pMain log subCmd
        | Some (Net_Fw subCmd) ->
            netFwMain log subCmd
        | Some (Net_Fw_Ref subCmd) ->
            netFwRefMain log subCmd
        | Some _ ->
            fun _ -> Error (InvalidArgsState "unknown sub command")
        | None ->
            fun _ ->  Error (InvalidArgsState "specify one command")

    let globalArgs =
        match Environment.GetEnvironmentVariable("DOTNET_PROJ_INFO_MSBUILD_BL") with
        | "1" -> MSBuild.MSbuildCli.Switch("bl") :: globalArgs
        | _ -> globalArgs

    let exec getArgs additionalArgs = attempt {
        let msbuildExec =
            let projDir = Path.GetDirectoryName(projPath)
            msbuild msbuildHost (runCmd log projDir)

        let! r =
            projPath
            |> getProjectInfoBySdk log msbuildExec getArgs additionalArgs
            |> Result.mapError ExecutionError

        return r
        }

    let! r = exec cmd globalArgs

    let out =
        match r with
        | FscArgs args -> args
        | CscArgs args -> args
        | P2PRefs args -> args
        | Properties args -> args |> List.map (fun (x,y) -> sprintf "%s=%s" x y)
        | Items args ->
            [ for item in args do
                yield sprintf "%s=%s" item.Name item.Identity
                for (metadata, value) in item.Metadata do
                    yield sprintf "%s.%s=%s" item.Name (getItemsModifierMSBuildProperty metadata) value ]
        | ResolvedP2PRefs args ->
            let optionalTfm t =
                t |> Option.map (sprintf " (%s)") |> Option.defaultValue ""
            args |> List.map (fun r -> sprintf "%s%s" r.ProjectReferenceFullPath (optionalTfm r.TargetFramework))
        | ResolvedNETRefs args -> args
        | InstalledNETFw args -> args

    out |> List.iter (printfn "%s")

    return r
    }

let wrapEx m f a =
    try
        f a
    with ex ->
        Error (RaisedException (ex, m))

let (|HelpRequested|_|) (ex: ArguParseException) =
    match ex.ErrorCode with
    | Argu.ErrorCode.HelpText -> Some ex.Message
    | _ -> None

[<EntryPoint>]
let main argv =
    match wrapEx "uncaught exception" (realMain >> runAttempt) argv with
    | Ok _ -> 0
    | Error err ->
        match err with
        | InvalidArgs (HelpRequested helpText) ->
            printfn "dotnet-proj."
            printfn " "
            printfn "%s" helpText
            0
        | InvalidArgs ex ->
            printfn "%s" (ex.Message)
            1
        | InvalidArgsState message ->
            printfn "%s" message
            printfn "see --help for more info"
            2
        | ProjectFileNotFound projPath ->
            printfn "project file '%s' not found" projPath
            3
        | GenericError message ->
            printfn "%s" message
            4
        | RaisedException (ex, message) ->
            printfn "%s:" message
            printfn "%A" ex
            6
        | ExecutionError (MSBuildFailed (i, r)) ->
            printfn "%i %A" i r
            7
        | ExecutionError (UnexpectedMSBuildResult r) ->
            printfn "%A" r
            8
        | ExecutionError (MSBuildSkippedTarget) ->
            printfn "internal error, target was skipped"
            9
