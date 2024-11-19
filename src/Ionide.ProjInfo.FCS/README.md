# Ionide.ProjInfo.FCS

This is a helper library that provides APIs to map Ionide.ProjInfo.Types.ProjectOptions instances to FSharp.Compiler.CodeAnalysis.FSharpProjectOptions instances.

Assuming you've already done the steps in Ionide.ProjInfo to get the ProjectOptions instances, you can use the following code to get the FSharpProjectOptions for those instances efficiently

```fsharp
open Ionide.ProjInfo

let fcsProjectOptions = FCS.mapManyOptions projectOptions
```