namespace Dotnet.ProjInfo.Workspace

[<RequireQualifiedAccess>]
module Option =

  let getOrElse defaultValue option =
    match option with
    | None -> defaultValue
    | Some x -> x
