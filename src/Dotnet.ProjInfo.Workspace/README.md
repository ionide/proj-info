# Dotnet.ProjInfo.Workspace

Library to load a list of projects or a sln, with info about the projects.

Allow to initialize FCS arguments for projects

Support both .NET Sdk projects (slim fsproj/csproj) and Old Sdk projects (verbose fsproj/csproj)

Allow to discover the installed MSBuild

Allow to initialize FCS arguments for .fsx script files

Easy to use, no big deps (no msbuild, no FCS). MSBuild is executed out of
process, as console app

## Project loading

Both loading an .sln or a list of projects is supported.

For each projects is possibile to specify properties (like `Configuration`) or
the target framework.
Loading projects who support multi targeting (`TargetFrameworks`) will load
the correct framework for the project references.

Will traverse all language projects

NEXT

- support VB compiler args, atm only F# and C# are supported

## Info about each project

Will gather info about the project, like the `None`, `Content`, `Compile` items 
to show.

Compute the tree to visualize (like the VS Solution Explorer) considering modifiers like `Link` (renamed file) and `DependsUpon` (put the file as a node of another file)

NEXT

- check invalid visual tree, like `["a/b.fs"; "c.fs"; "a/d.fs"]`, because `a` node cannot be visualized two times

### Load a solution (.sln)

Load a Visual Studio solution (.sln file)

NEXT

- because sln specify graph of dependencies, is possibile to known what projects
are leaf (not used as project references) so these can be used as starting point
to visit the graph of project references

### Load a list of projects

Load a list of projects

## Initialize FCS arguments

Initialize the types needed by FCS.

### For projects

The types contains the compiler arguments and project references

### For fsx script files

Allow to specify a .NET target framework, and load correct references

## Discover installed MSBuild

Use `vswhere` to discover installed MSBuild versions
