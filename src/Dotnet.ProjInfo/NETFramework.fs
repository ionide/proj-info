module Dotnet.ProjInfo.NETFramework

open System
open System.IO

let netifyTargetFrameworkVersion (v: string) =
    // TODO better translation for strange tfm
    if v.StartsWith("v") then
        "net" + v.TrimStart('v').Replace(".", "")
    else
        v
