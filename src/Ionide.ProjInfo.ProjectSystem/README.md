# Ionide.ProjInfo.ProjectSystem

This library provides helpers for operating an entire project system based on the data structures returned by the Ionide.ProjInfo library.

The main entrypoint is the `ProjectController` API in the Ionide.ProjInfo.ProjectSystem workspace:

```fsharp
type ProjectController(toolsPath: ToolsPath, workspaceLoaderFactory: ToolsPath -> IWorkspaceLoader) =
    ...
```

From there you can load specific projects, get their dependencies, and more.
