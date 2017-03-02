# Examples usage of tool

First restore the sample the packages and the tool

```
dotnet restore tools
dotnet restore c1
```

NOTE Because is a dotnetcli command, must be executed from same directory of project
     where is declared

```
cd tools
```

And

```
dotnet proj-info -p ..\c1\c1.fsproj --project-refs
```

or

```
dotnet proj-info -p ..\c1\c1.fsproj --fsc-args
```
