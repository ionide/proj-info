# Examples usage of tool

First restore the sample the packages and the tool

```bash
dotnet restore tools
dotnet restore sdk1/c1
```

**NOTE** Because is a dotnet cli command, must be executed from same directory of project
     where is declared. The project can be passed, if not it search the working directory
     for a project (single .*proj)

```bash
cd tools
```


Some examples of commands:

```bash
dotnet proj-info ../sdk1/c1/c1.fsproj --project-refs
```

or

```bash
dotnet proj-info ../sdk1/c1/c1.fsproj --fsc-args
```

or

```bash
dotnet proj-info ../sdk1/c1/c1.fsproj --get-property OutputType Version Configuration
```

It's possibile to pass usual .NET Core Tools arguments (like `-c`, `-f`, `-r`).

See `--help` for more info

```bash
dotnet proj-info ../sdk1/l1/l1.fsproj -gp MyCustomProp OutputType Version Configuration -c Release
```

### old sdk (verbose fsproj)

These are supported too from version 0.8

NOTE: require `msbuild` in PATH, or pass `--msbuild` with full path to msbuild

Like before

```
dotnet proj-info ../oldsdk/l1/l1.fsproj --fsc-args
```

Or to get properties passing the msbuild to use

```
dotnet proj-info ../oldsdk/l1/l1.fsproj -gp MyCustomProp OutputType Version Configuration --msbuild "C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe"
```
