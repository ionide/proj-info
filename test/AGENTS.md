1. If there is a global.json file in this folder. Delete it. It's from a previous test run.
2. Use `./build.sh test` or `\.\build.cmd test` to run the entire test suite. The tests are runtime dependent and the build script will run tests for net8, net9, and net10.
3. If a run fails and leaves a `global.json` behind, delete `test/global.json` before re-running.
4. If you're only needing to run against a specific runtime, you can use the target of `test:net10.0`, `test:net9.0` or `test:net8.0` for `build.sh` such as `build.sh test:net8.0`.
5. If you need to filter for a specific test, you can update `build/Program.fs` with `"FullyQualifiedName~insertTestNameHere"` and run the specific TFM.
6. Sometimes filtering mechanism isn't easy to configure to get the right subset of tests. We use expecto which allows skipping or focusing tests by editing source code.
    * To skip tests reference https://github.com/haf/expecto?tab=readme-ov-file#pending-tests
    * To focus tests reference https://github.com/haf/expecto?tab=readme-ov-file#focusing-tests
