namespace Dotnet.ProjInfo.Workspace

[<RequireQualifiedAccess>]
module Option =

  let getOrElse defaultValue option =
    match option with
    | None -> defaultValue
    | Some x -> x

module Utils =

  let runProcess (log: string -> unit) (workingDir: string) (exePath: string) (args: string) =
      let psi = System.Diagnostics.ProcessStartInfo()
      psi.FileName <- exePath
      psi.WorkingDirectory <- workingDir
      psi.RedirectStandardOutput <- true
      psi.RedirectStandardError <- true
      psi.Arguments <- args
      psi.CreateNoWindow <- true
      psi.UseShellExecute <- false

      use p = new System.Diagnostics.Process()
      p.StartInfo <- psi

      p.OutputDataReceived.Add(fun ea -> log (ea.Data))

      p.ErrorDataReceived.Add(fun ea -> log (ea.Data))

      // printfn "running: %s %s" psi.FileName psi.Arguments

      p.Start() |> ignore
      p.BeginOutputReadLine()
      p.BeginErrorReadLine()
      p.WaitForExit()

      let exitCode = p.ExitCode

      exitCode, (workingDir, exePath, args)
