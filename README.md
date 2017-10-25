# dotnet-proj-info

- dotnet cli tool: `dotnet proj-info`
- as library: `Dotnet.ProjInfo`

Support:

- get properties
- get project to project references
- get fsc command line arguments

Support both dotnet/sdk style projects (slim proj, usually .net core) and old sdk projects (verbose proj, usually .NET)

Works on mono and windows, and allow to specify the `dotnet` or `msbuild` to use

Add usual args of .NET cli, like `-c`, `-f`, `-r`

see [examples](https://github.com/enricosada/dotnet-proj-info/tree/master/examples) directory

## Build

Run:

```bash
dotnet build
```

To run tests:

```bash
dotnet test -v n
```

To create packages:

```bash
dotnet pack
```

will create packages in `artifacts\nupkgs`

pass `/p:Version=1.2.3` to create a package with version `1.2.3`

