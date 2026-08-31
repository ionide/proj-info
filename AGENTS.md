# Agent Guide for Ionide.ProjInfo
1. Restore local tools before anything else: `dotnet tool restore`.
2. Standard build: `dotnet build ionide-proj-info.sln` (FAKE build target `dotnet run --project build -- -t Build`).
3. Full test matrix: `dotnet run --project build -- -t Test` (runs net8/net9/net10 with temporary `global.json`).
4. Single test project: `dotnet test test/Ionide.ProjInfo.Tests/Ionide.ProjInfo.Tests.fsproj --filter "FullyQualifiedName~<TestName>"` after ensuring the desired SDK via `global.json` and `BuildNet*` env vars.
5. Formatting uses Fantomas; prefer `dotnet run --project build -- -t CheckFormat`, and run `-t Format` only when necessary.
6. Repo follows .editorconfig: 4-space indent, LF endings, final newline; XML projects/yaml use 2 spaces.
7. F# formatting: Stroustrup braces, 240 char max line, limited blank lines, arrays/lists wrap after one item, multiline lambdas close on newline.
8. Naming: descriptive PascalCase for types/modules, camelCase for values/parameters, UPPER_CASE for constants/environment keys.
9. Imports: keep `open` statements grouped at top, ordered System → third-party → project namespaces; remove unused opens.
10. Types: prefer explicit types on public members and when inference harms clarity; use records/DU for shape safety.
11. Error handling: use `Result`/`Choice` for recoverable states, raise exceptions only when failing fast; log via FsLibLog where available.
12. Async workflows: keep side effects isolated; favor `task {}` or `async {}` consistently per module.
13. Tests use Expecto; follow `TestAssets.fs` helpers and keep assertions expressive.
14. Avoid introducing new dependencies without discussion; stick to existing FAKE pipeline for packaging/pushing.
15. Copilot rules apply: follow `.github/copilot-instructions.md` for architecture pointers and release workflow awareness.
16. No Cursor-specific rules present; if they appear later under `.cursor/`, integrate them here.
17. Always update CHANGELOG for user-facing changes and confirm Code of Conduct compliance.
18. Document CLI/tool behavior changes in respective `README.md` files under `src/*`.
19. Keep public APIs backward compatible; prefer additive changes and guard feature flags.
20. When unsure about SDK/runtime alignment, run `dotnet --version` and adjust `global.json` to match test needs.


## Resources

### Core Documentation
- [FsAutoComplete GitHub Repository](https://github.com/ionide/FsAutoComplete)
- [LSP Specification](https://microsoft.github.io/language-server-protocol/)
- [F# Compiler Service Documentation](https://fsharp.github.io/FSharp.Compiler.Service/)

### F# Development Guidelines
- [F# Style Guide](https://docs.microsoft.com/en-us/dotnet/fsharp/style-guide/)
- [F# Formatting Guidelines](https://docs.microsoft.com/en-us/dotnet/fsharp/style-guide/formatting)
- [F# Component Design Guidelines](https://docs.microsoft.com/en-us/dotnet/fsharp/style-guide/component-design-guidelines)

### Project-Specific Guides
- [Creating a New Code Fix Guide](./docs/Creating%20a%20new%20code%20fix.md)
- [Ionide.ProjInfo Documentation](https://github.com/ionide/proj-info)
- [Fantomas Configuration](https://fsprojects.github.io/fantomas/)

### Related Tools
- [FSharpLint](https://github.com/fsprojects/FSharpLint/) - Static analysis tool
- [Paket](https://fsprojects.github.io/Paket/) - Dependency management
- [FAKE](https://fake.build/) - Build automation (used for scaffolding)


### MCP Tools

> [!IMPORTANT]

You have access to a long-term memory system via the Model Context Protocol (MCP) at the endpoint `memorizer`. Use the following tools:
- `store`: Store a new memory. Parameters: `type`, `content` (markdown), `source`, `tags`, `confidence`, `relatedTo` (optional, memory ID), `relationshipType` (optional).
- `search`: Search for similar memories. Parameters: `query`, `limit`, `minSimilarity`, `filterTags`.
- `get`: Retrieve a memory by ID. Parameter: `id`.
- `getMany`: Retrieve multiple memories by their IDs. Parameter: `ids` (list of IDs).
- `delete`: Delete a memory by ID. Parameter: `id`.
- `createRelationship`: Create a relationship between two memories. Parameters: `fromId`, `toId`, `type`.
Use these tools to remember, recall, relate, and manage information as needed to assist the user. You can also manually retrieve or relate memories by their IDs when necessary.
