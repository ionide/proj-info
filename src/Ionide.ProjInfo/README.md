# Ionide.Proj-Info

This project loads some MSBuild specific assemblies at runtime so that you can use an existing MSBuild installation instead of (incorrectly) bundling it yourself. Somewhat similar to how [MSBuildLocator](https://github.com/microsoft/MSBuildLocator) loads the correct assemblies.
Because of this you need to add a direct dependency on `Microsoft.Build.Framework` and `NuGet.Frameworks` but keep excluded them at runtime.

```
<PackageReference Include="Microsoft.Build.Framework" Version="17.2.0" ExcludeAssets="runtime" PrivateAssets="all" />
<PackageReference Include="NuGet.Frameworks" Version="6.2.1" ExcludeAssets="runtime" PrivateAssets="all" />
<PackageReference Include="Ionide.ProjInfo" Version="some_version" />
```

Next, you first need to initialize the MsBuild integration.

```fsharp
open Ionide.ProjInfo

let projectDirectory: DirectoryInfo = yourProjectOrSolutionFolder
let toolsPath = Init.init projectDirectory None
```

With the `toolsPath` you can create a `loader`

```fsharp
let defaultLoader: IWorkspaceLoader = WorkspaceLoader.Create(toolsPath, [])
// or
let graphLoader: IWorkspaceLoader = WorkspaceLoaderViaProjectGraph.Create(toolsPath, [])
```

Using the `IWorkspaceLoader` you can load projects or solutions.
Events are being emitted while projects/solutions are loaded.
You typically want to subscribe to this before you load anything.

```fsharp
let subscription: System.IDisposable = defaultLoader.Notifications.Subscribe(fun msg -> printfn "%A" msg)
let projectOptions = loader.LoadProjects([ yourFsProjPath ]) |> Seq.toArray
```

From here consider using Ionide.ProjInfo.FCS to map the `projectOptions` to F# Compiler `ProjectOptions`, or use the `projectOptions` directly to get information about the project.
