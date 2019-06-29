# Examples usage of tool

First install the tool

```bash
dotnet tool install -g dotnet-proj
```

See help with

```
dotnet proj --help
```

## .NET Sdk projects

In

```
cd sdk2
```

Some examples of commands i, like

```bash
dotnet proj p2p c1/c1.fsproj
```

or

```bash
dotnet proj fsc-args c1/c1.fsproj
dotnet proj csc-args l2/l2.csproj
```

or

```bash
dotnet proj prop c1/c1.fsproj -get OutputType -get Version -get Configuration
```

It's possibile to pass usual .NET Core Tools arguments (like `-c`, `-f`, `-r`).

```bash
dotnet proj prop c1/c1.fsproj -get OutputType -c Release
```

And by default search for projects in current directory

```
cd l1
dotnet proj fsc-args -c Release
```

## Old Sdk projects (verbose fsproj)

**NOTE** require `msbuild` in PATH (like in VS Command Prompt), or pass `--msbuild` with full path to msbuild

In

```
cd oldsdk
```

Like before

```
dotnet proj fsc-args l1/l1.fsproj
```

You can specify the msbuild to use

```
dotnet proj prop l1/l1.fsproj -get DocumentationFile --msbuild "C:\Program Files (x86)\Microsoft Visual Studio\2017\Enterprise\MSBuild\15.0\Bin\MSBuild.exe"
```

## .NET info

**NOTE** require `msbuild` in PATH (like in VS Command Prompt), or pass `--msbuild` with full path to msbuild

Run

```
dotnet proj net-fw
```

You can specify the msbuild to use

```
dotnet proj net-fw-ref System.Xml -f v3.5 --msbuild "C:\Program Files (x86)\Microsoft Visual Studio\2017\Enterprise\MSBuild\15.0\Bin\MSBuild.exe"
```
