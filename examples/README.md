# Examples usage of tool

First restore the sample the packages and the tool

```
dotnet restore tools
dotnet restore c1
```

**NOTE** Because is a dotnet cli command, must be executed from same directory of project
     where is declared. The project can be passed, if not it search the working directory
     for a project (single .*proj)

```
cd tools
```

and

```
dotnet proj-info ..\c1\c1.fsproj --project-refs
```

or

```
dotnet proj-info ..\c1\c1.fsproj --fsc-args
```

or

```
dotnet proj-info ..\c1\c1.fsproj --get-property OutputType Version Configuration
```

It's possibile to pass usual .NET Core Tools arguments (like `-c`, `-f`, `-r`).

See `--help` for more info

```
dotnet proj-info ..\l1\l1.fsproj --gp MyCustomProp OutputType Version Configuration -c Release
```
