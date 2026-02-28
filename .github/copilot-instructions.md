# Copilot Instructions for Ionide.ProjInfo

## Project Overview
- **Ionide.ProjInfo** is a set of F# libraries and tools for parsing and evaluating `.fsproj` and `.sln` files, used by F# tooling (e.g., Ionide, FSAC, Fable, FSharpLint).
- Major components:
  - `Ionide.ProjInfo`: Core project/solution parsing logic (uses Microsoft.Build APIs).
  - `Ionide.ProjInfo.FCS`: Maps project data to FSharp.Compiler.Service types.
  - `Ionide.ProjInfo.ProjectSystem`: High-level project system for editor tooling (change tracking, notifications, caching).
  - `Ionide.ProjInfo.Tool`: CLI for debugging project cracking.

## Build & Test Workflows
- **Restore tools:** `dotnet tool restore`
- **Build solution:** `dotnet build ionide-proj-info.sln`
- **Run all tests:** `dotnet run --project build -- Test`
- **Multi-TFM testing:**
    - For specific TFM:
        - LTS (net8.0) `dotnet run --project build -- Test:net8.0`
        - STS (net9.0) `dotnet run --project build -- Test:net9.0`

- **Test assets:** Test projects in `test/examples/` cover a wide range of real-world project structures (multi-TFM, C#/F#, old/new SDK, solution filters, etc.).

## Key Patterns & Conventions
- **Project loading:** Prefer using the MSBuild loader; use `--graph` for graph-based loading in CLI tool.
- **Output formats:** CLI tool supports structured text, FCS options (`--fcs`), or JSON (`--serialize`).
- **Cross-language:** Handles both F# and C# projects/references.
- **Persistent caching:** ProjectSystem caches data for fast reloads.
- **Testing:** Uses Expecto for tests; see `test/Ionide.ProjInfo.Tests/` for patterns.
- **Release process:** Update `CHANGELOG.md`, tag, and push (see `CONTRIBUTING.md`).

## Integration Points
- Consumed by Fable, FSAC, Ionide, FSharpLint, and F# Formatting.
- External dependencies: Microsoft.Build, FSharp.Compiler.Service (FCS).
- Nightly builds may require custom NuGet feeds (see `CONTRIBUTING.md`).

## Where to Look
- **Architecture:** See `README.md` (root), `src/` for main libraries, `test/examples/` for project scenarios.
- **Developer workflow:** `CONTRIBUTING.md` for build/test/release details.
- **Tool usage:** `src/Ionide.ProjInfo.Tool/README.md` and `Program.fs`.
- **Test patterns:** `test/Ionide.ProjInfo.Tests/` and helpers in `FileUtils.fs`.

## Code of Conduct
- See `CODE_OF_CONDUCT.md` for community standards.

---
For more, see https://github.com/ionide/dotnet-proj-info and linked docs.


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
