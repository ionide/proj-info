namespace Dotnet.ProjInfo.Workspace

module DotnetProjInfoInspectHelpers =

  let (|MsbuildOk|_|) x =
    match x with
    | Ok x -> Some x
    | Error _ -> None

  let (|MsbuildError|_|) x =
    match x with
    | Ok _ -> None
    | Error x -> Some x

  let msbuildPropBool (s: string) =
    match s.Trim() with
    | "" -> None
    | Dotnet.ProjInfo.Inspect.MSBuild.ConditionEquals "True" -> Some true
    | _ -> Some false

  let msbuildPropStringList (s: string) =
    match s.Trim() with
    | "" -> []
    | Dotnet.ProjInfo.Inspect.MSBuild.StringList list  -> list
    | _ -> []


module internal NETFrameworkInfoProvider =

  open System
  open System.IO
  open Dotnet.ProjInfo
  open Dotnet.ProjInfo.Inspect

  let getInstalledNETVersions (msbuildHost: MSBuildExePath) =

    let log = ignore

    let projPath =
        //create the proj file
        NETFrameworkInfoFromMSBuild.createEnvInfoProj ()
        |> Path.GetFullPath

    let projDir = Path.GetDirectoryName(projPath)

    let cmd = NETFrameworkInfoFromMSBuild.installedNETFrameworks

    let runCmd exePath args = Utils.runProcess log projDir exePath (args |> String.concat " ")

    let msbuildExec =
        msbuild msbuildHost runCmd

    let result =
        projPath
        |> getProjectInfo log msbuildExec cmd []

    match result with
    | Ok (Dotnet.ProjInfo.Inspect.GetResult.InstalledNETFw fws) ->
        fws
    | Ok x ->
        failwithf "error getting msbuild info: unexpected %A" x
    | Error r ->
        failwithf "error getting msbuild info: unexpected %A" r

  let private defaultReferencesForNonProjectFiles () =
    // ref https://github.com/fsharp/FSharp.Compiler.Service/blob/1f497ef86fd5d0a18e5a935f3d16984fda91f1de/src/fsharp/CompileOps.fs#L1801
    // This list is the default set of references for "non-project" files
    
    // TODO make somehow this list public on FCS and use that directly instead of hardcode it in FSAC

    let GetDefaultSystemValueTupleReference () =
      //TODO check by tfm
      None

    // from https://github.com/fsharp/FSharp.Compiler.Service/blob/1f497ef86fd5d0a18e5a935f3d16984fda91f1de/src/fsharp/CompileOps.fs#L1803-L1832
    [
          yield "System"
          yield "System.Xml" 
          yield "System.Runtime.Remoting"
          yield "System.Runtime.Serialization.Formatters.Soap"
          yield "System.Data"
          yield "System.Drawing"
          yield "System.Core"
          // These are the Portable-profile and .NET Standard 1.6 dependencies of FSharp.Core.dll.  These are needed
          // when an F# sript references an F# profile 7, 78, 259 or .NET Standard 1.6 component which in turn refers 
          // to FSharp.Core for profile 7, 78, 259 or .NET Standard.
          yield "System.Runtime" // lots of types
          yield "System.Linq" // System.Linq.Expressions.Expression<T> 
          yield "System.Reflection" // System.Reflection.ParameterInfo
          yield "System.Linq.Expressions" // System.Linq.IQueryable<T>
          yield "System.Threading.Tasks" // valuetype [System.Threading.Tasks]System.Threading.CancellationToken
          yield "System.IO"  //  System.IO.TextWriter
          //yield "System.Console"  //  System.Console.Out etc.
          yield "System.Net.Requests"  //  System.Net.WebResponse etc.
          yield "System.Collections" // System.Collections.Generic.List<T>
          yield "System.Runtime.Numerics" // BigInteger
          yield "System.Threading"  // OperationCanceledException
          // always include a default reference to System.ValueTuple.dll in scripts and out-of-project sources
          match GetDefaultSystemValueTupleReference() with 
          | None -> ()
          | Some v -> yield v

          yield "System.Web"
          yield "System.Web.Services"
          yield "System.Windows.Forms"
          yield "System.Numerics" 
    ]

  let getAdditionalArgumentsBy (msbuildHost: MSBuildExePath) targetFramework =
    let refs =
      let log = ignore

      let projPath =
        //create the proj file
        NETFrameworkInfoFromMSBuild.createEnvInfoProj ()
        |> Path.GetFullPath

      let projDir = Path.GetDirectoryName(projPath)

      let allRefs = defaultReferencesForNonProjectFiles ()

      let props =
        targetFramework
        |> fun tfm -> "TargetFrameworkVersion", tfm
        |> List.singleton
        |> List.map (Dotnet.ProjInfo.Inspect.MSBuild.MSbuildCli.Property)

      let cmd () = NETFrameworkInfoFromMSBuild.getReferencePaths allRefs

      let runCmd exePath args = Utils.runProcess log projDir exePath (args |> String.concat " ")

      let msbuildExec =
        msbuild msbuildHost runCmd

      let result =
        projPath
        |> getProjectInfo log msbuildExec cmd props

      match result with
      | Ok (Dotnet.ProjInfo.Inspect.GetResult.ResolvedNETRefs resolvedRefs) ->
          resolvedRefs
      | Ok x ->
          failwithf "error getting msbuild info: unexpected %A" x
      | r ->
          failwithf "error getting msbuild info: unexpected %A" r

    [ yield "--simpleresolution"
      yield "--noframework"
      yield! refs |> List.map (sprintf "-r:%s") ]

