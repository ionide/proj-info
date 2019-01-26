namespace Dotnet.ProjInfo.Workspace

module FSharpCompilerServiceCheckerHelper =

  open System.IO

  let private isFSharpCore (s : string) = s.EndsWith "FSharp.Core.dll"

  let private fallbackFsharpCore =
    //TODO no, use another way. can be wrong by tfm, etc
    let dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
    Path.Combine(dir, "FSharp.Core.dll")

  let internal ensureCorrectFSharpCore (options: string[]) =
    let fsharpCores, others = Array.partition isFSharpCore options

    // ensure that there is only one fsharpcore ref provided
    let fsharpCoreRef =
      match fsharpCores with
      | [||] -> sprintf "-r:%s" fallbackFsharpCore
      | [| ref |] -> ref
      | refs -> Array.head refs

    [| yield fsharpCoreRef
       yield! others |]

module FSharpCompilerServiceChecker =

  /// checker.GetProjectOptionsFromScript(file, source, otherFlags = additionaRefs, assumeDotNetFramework = true)
  type CheckerGetProjectOptionsFromScriptArgs = string * string * (string array) * bool
  type CheckerGetProjectOptionsFromScript<'a, 'b> = CheckerGetProjectOptionsFromScriptArgs -> Async<'a * 'b>

  let getProjectOptionsFromScript (checkerGetProjectOptionsFromScript: CheckerGetProjectOptionsFromScript<'a, 'b>) file source targetFramework = async {

    // let targetFramework = NETFrameworkInfoProvider.netReferecesAssembliesTFMLatest ()

    let additionaRefs =
      NETFrameworkInfoProvider.additionalArgumentsBy targetFramework
      |> Array.ofList

    // TODO SRTP
    let! (rawOptions, _) = checkerGetProjectOptionsFromScript (file, source, additionaRefs, true)

    let mapOtherOptions opts =
      opts
      |> FSharpCompilerServiceCheckerHelper.ensureCorrectFSharpCore
      |> Array.distinct

    return rawOptions, mapOtherOptions

  }