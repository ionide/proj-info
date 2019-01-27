namespace Dotnet.ProjInfo.Workspace.FCS

open Dotnet.ProjInfo.Workspace
open Microsoft.FSharp.Compiler.SourceCodeServices

type FCSBinder () =

    // let projectLoadedSuccessfully projectFileName response =
    //     let project =
    //         match state.Projects.TryFind projectFileName with
    //         | Some prj -> prj
    //         | None ->
    //             let proj = new Project(projectFileName, onChange)
    //             state.Projects.[projectFileName] <- proj
    //             proj
    //     ()

    member this.Bind(fcs: FSharpChecker, workspace: Loader) =
        ()
