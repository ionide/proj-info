set Configuration=Release
set "PackageOutputPath=%~dp0\artifacts\nupkgs"

dotnet restore "%~dp0\src\dotnet-proj-info\dotnet-proj-info.fsproj"
@if ERRORLEVEL 1 exit /b 1

dotnet pack "%~dp0\src\Dotnet.ProjInfo\Dotnet.ProjInfo.fsproj"
@if ERRORLEVEL 1 exit /b 1

dotnet pack "%~dp0\src\dotnet-proj-info\dotnet-proj-info.fsproj"
@if ERRORLEVEL 1 exit /b 1

exit /b 0
