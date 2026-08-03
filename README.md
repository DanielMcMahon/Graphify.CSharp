## Graphify.CSharp

Roslyn-powered C# knowledge graph builder inspired by [Graphify](https://github.com/Graphify-Labs/graphify).

Instead of Tree-sitter over many languages, Graphify.CSharp uses the C# compiler platform to build a **semantic** graph: types, methods, calls, inheritance, DI constructor heuristics, and project references. The graph is stored in SQLite and queried through a CLI or MCP server.

### Why Roslyn instead of a generic AST parser?

- Resolves method calls to real symbols (not just text matches)
- Understands inheritance, interface implementation, overrides
- Sees project/assembly references
- Flags ambiguous edges (virtual dispatch, overload resolution) like Graphify's `AMBIGUOUS` confidence

### Quick start

```bash
# From repo root
dotnet build

# Build a graph
dotnet run --project src/Graphify.CSharp.Cli -- build /path/to/YourSolution.sln

# Query symbols
dotnet run --project src/Graphify.CSharp.Cli -- query OrderService

# Find a knowledge path
dotnet run --project src/Graphify.CSharp.Cli -- path OrderService IOrderRepository

# Explain relationships
dotnet run --project src/Graphify.CSharp.Cli -- explain OrderService

# Gap analysis when no path exists
dotnet run --project src/Graphify.CSharp.Cli -- gaps MyController MyRepository

# Export Graphify-compatible JSON
dotnet run --project src/Graphify.CSharp.Cli -- export --db .graphify/graph.db --output .graphify/graph.json

# Watch for file changes and rebuild automatically
dotnet run --project src/Graphify.CSharp.Cli -- watch /path/to/YourSolution.sln --output .graphify/graph.db --json .graphify/graph.json

# Trace ASP.NET endpoint -> handler -> repository flows
dotnet run --project src/Graphify.CSharp.Cli -- flows "GET /jobs" --db .graphify/graph.db

# UI access paths for server-rendered / imperative UI (Playwright prep)
dotnet run --project src/Graphify.CSharp.Cli -- ui-access "invoice-link" --surface OrderDetailsPage
dotnet run --project src/Graphify.CSharp.Cli -- ui-surface OrderDetailsPage

# Interactive graph UI (like Graphify's graph.html)
dotnet run --project src/Graphify.CSharp.Cli -- serve
# Open http://127.0.0.1:5173
```

Output defaults to `.graphify/graph.db`.

### One-command agent setup

The fastest way to make any AI agent architecture-aware in a C# repo:

```bash
cd /path/to/your/csharp/project
dotnet run --project /path/to/Graphify.CSharp/src/Graphify.CSharp.Cli -- init
```

This single command:

1. Finds your `.sln` and builds `.graphify/graph.db`
2. Writes `.graphify/config.json` and `GRAPH_REPORT.md`
3. Installs a **Cursor skill**, **MCP server**, and **always-on rule** into `.cursor/`
4. Adds a **git post-commit hook** to rebuild the graph after commits

After `init`, restart Cursor and ask naturally:

- "How does `OfferJob` work?"
- "Who calls `INotifier`?"
- "Trace the flow from the jobs API to the database"

The agent calls the `HowDoesItWork` MCP tool automatically — no manual CLI needed.

### Install into AI agents manually

Like [Graphify's `graphify install`](https://github.com/Graphify-Labs/graphify), Graphify.CSharp can install a **Cursor agent skill** and **MCP server config** in one command:

```bash
# Install skill + MCP for Cursor, Copilot, and OpenCode (global)
dotnet run --project src/Graphify.CSharp.Cli -- install

# Install only into the current repo
dotnet run --project src/Graphify.CSharp.Cli -- install --project --db .graphify/graph.db

# Selective install
dotnet run --project src/Graphify.CSharp.Cli -- install --cursor-skill --cursor-mcp
```

| Target | What gets installed |
|--------|---------------------|
| Claude Code skill | `~/.claude/skills/graphify-csharp/SKILL.md` |
| Cursor skill | `~/.cursor/skills/graphify-csharp/SKILL.md` (or `.cursor/skills/...` with `--project`) |
| Cursor rule | `.cursor/rules/graphify-csharp.mdc` (always-on architecture guidance) |
| Cursor MCP | `.cursor/mcp.json` |
| Copilot MCP | `~/.copilot/mcp-config.json` |
| OpenCode MCP | `~/.config/opencode/opencode.json` |
| Git hook | `.git/hooks/post-commit` (auto-rebuild after commits) |

The skill teaches agents when and how to build, query, path, explain, and trace flows through your C# graph. MCP configs are merged into existing files — existing servers are preserved.

Restart your AI client after install to pick up MCP changes.

### Interactive UI

The `serve` command launches a local web UI with a force-directed graph (similar to Graphify's `graph.html`):

- **Pan/zoom/drag** nodes
- **Search** symbols and focus the graph on them
- **Click** a node to see callers, callees, and references in the sidebar
- **Double-click** a node to expand its neighborhood
- **Filter** by relation type (`calls`, `references`, `inherits`, etc.)

```bash
dotnet run --project src/Graphify.CSharp.Cli -- serve --db .graphify/graph.db --port 5173
```

### MCP server (any AI client)

The MCP server uses **stdio** transport, which works with Cursor, GitHub Copilot, OpenCode, Codex, and other MCP-compatible clients. Each client spawns its own server process; they can all run at the same time and share the same graph when pointed at the same database file.

**Tools:** `Investigate`, `HowDoesItWork`, `EnsureGraph`, `GetGraphStatus`, `BuildGraph`, `QuerySymbol`, `FindPath`, `ExplainSymbol`, `FindFlows`, `FindGaps`, `GetUiAccessPath`, `ListSurfaceUi`, `ExportUiPrerequisites`.

`GetUiAccessPath` is for **server-rendered UI / Playwright prep** — it traces prerequisites, visibility gates, selector hints, and navigation for dynamically generated UI (WebForms, imperative HTML builders, etc.).

`Investigate` is the default entry point for open-ended questions — it runs search, explanation, impact analysis, and writes a handoff file to `.graphify/investigations/`.

`TraceTable` is for **database migration** work — give it a table name and it traces backwards to entities, SQL/Dapper query sites, ASPX pages, file-path columns, and `System.IO`/blob storage touchpoints.

**Database path resolution** (for all query tools and `BuildGraph` output):

1. Explicit `databasePath` / `output` argument in the tool call
2. `GRAPHIFY_DB` environment variable
3. `.graphify/graph.db` (relative to the client's working directory)

Set `GRAPHIFY_DB` once in each client's MCP config so every tool uses the same graph without repeating the path:

```bash
export GRAPHIFY_DB="/absolute/path/to/your/project/.graphify/graph.db"
```

Build the MCP server once, then reference the DLL in config (faster than `dotnet run`):

```bash
dotnet build src/Graphify.CSharp.Mcp
# DLL: src/Graphify.CSharp.Mcp/bin/Debug/net9.0/Graphify.CSharp.Mcp.dll
```

#### Cursor

`~/.cursor/mcp.json` or `.cursor/mcp.json` in your project:

```json
{
  "mcpServers": {
    "graphify-csharp": {
      "command": "dotnet",
      "args": ["/absolute/path/to/Graphify.CSharp/src/Graphify.CSharp.Mcp/bin/Debug/net9.0/Graphify.CSharp.Mcp.dll"],
      "env": {
        "GRAPHIFY_DB": "/absolute/path/to/your/project/.graphify/graph.db"
      }
    }
  }
}
```

#### GitHub Copilot (CLI / VS Code)

`~/.copilot/mcp-config.json` or `.vscode/mcp.json`:

```json
{
  "mcpServers": {
    "graphify-csharp": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["/absolute/path/to/Graphify.CSharp/src/Graphify.CSharp.Mcp/bin/Debug/net9.0/Graphify.CSharp.Mcp.dll"],
      "env": {
        "GRAPHIFY_DB": "/absolute/path/to/your/project/.graphify/graph.db"
      }
    }
  }
}
```

#### OpenCode

`~/.config/opencode/opencode.json`:

```json
{
  "mcp": {
    "graphify-csharp": {
      "type": "local",
      "command": [
        "dotnet",
        "/absolute/path/to/Graphify.CSharp/src/Graphify.CSharp.Mcp/bin/Debug/net9.0/Graphify.CSharp.Mcp.dll"
      ],
      "enabled": true,
      "environment": {
        "GRAPHIFY_DB": "/absolute/path/to/your/project/.graphify/graph.db"
      }
    }
  }
}
```

#### Codex and other stdio MCP clients

Use the same `command`, `args`, and `GRAPHIFY_DB` pattern as Copilot. If your client supports `type: "stdio"` with `command` + `args`, it should work without code changes.

#### Tips for multi-client use

- **Build the graph once** with the CLI or `BuildGraph`, then query from any client.
- Avoid running `BuildGraph` from two clients at the same time on the same `.db` file.
- Use an **absolute** `GRAPHIFY_DB` path so different clients aren't affected by their working directory.

### Graph model

| Relation | Meaning |
|----------|---------|
| `calls` | Method invocation |
| `inherits` | Base class |
| `implements` | Interface |
| `references` | Type usage |
| `injects` | Constructor parameter (DI heuristic) |
| `returns` | Method return type |
| `contains` | Namespace/type containment |
| `project_references` | Project dependency |
| `dispatches` | MediatR `ISender.Send(...)` |
| `handles` | MediatR `IRequestHandler<,>` / `INotificationHandler<>` |
| `publishes` | MediatR `ISender.Publish(...)` |
| `routes` | ASP.NET minimal API / controller route → handler |

Confidence: `Extracted` (compiler-resolved), `Ambiguous` (heuristic/unresolved), `Inferred` (reserved for future doc linking).

### Roadmap

- [x] Roslyn analyzer package for incremental IDE updates (`Graphify.CSharp.Analyzers`)
- [x] ASP.NET endpoint → handler → repository flow templates (`routes` edges + `flows` command)
- [x] MediatR specialized edges (`dispatches`, `handles`, `publishes`)
- [x] JSON export compatible with Graphify tooling (`export` command and `build --json`)
- [x] Watch mode to rebuild graph on file changes (`watch` command)

### Incremental analyzer

Reference the analyzer from a project to capture per-compilation call fragments under `obj/graphify/fragment.json`:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/Graphify.CSharp.Analyzers/Graphify.CSharp.Analyzers.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

The analyzer reports `GRAPHIFY001` info diagnostics with call-edge counts per assembly. Pair it with the `watch` command for continuous graph rebuilds during development.

### License

MIT
