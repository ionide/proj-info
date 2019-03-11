module TestsConfig

type TestSuiteConfig =
    { SkipFlaky: bool
      SkipKnownFailure: bool }

let flaky name = sprintf "%s [flaky]" name
let knownFailure name = sprintf "%s [known-failure]" name
