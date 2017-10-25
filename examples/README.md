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
dotnet proj-info ../sdk1/l1/l1.fsproj --gp MyCustomProp OutputType Version Configuration -c Release
```
