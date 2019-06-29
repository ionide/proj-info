module Dotnet.ProjInfo.Resources

open System

type internal Dummy() = inherit Object()

open System.Reflection

let getResourceFileAsString resourceName =
    let assembly = typeof<Dummy>.GetTypeInfo().Assembly

    use stream = assembly.GetManifestResourceStream(resourceName)
    match stream with
    | null -> failwithf "Resource '%s' not found in assembly '%s'" resourceName (assembly.FullName)
    | stream ->
        use reader = new IO.StreamReader(stream)

        reader.ReadToEnd()
