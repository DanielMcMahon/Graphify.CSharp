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

# Interactive graph UI (like Graphify's graph.html)
dotnet run --project src/Graphify.CSharp.Cli -- serve
# Open http://127.0.0.1:5173
```

Output defaults to `.graphify/graph.db`.

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

**Tools:** `BuildGraph`, `QuerySymbol`, `FindPath`, `ExplainSymbol`, `FindGaps`.

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

Confidence: `Extracted` (compiler-resolved), `Ambiguous` (heuristic/unresolved), `Inferred` (reserved for future doc linking).

### Roadmap

- [ ] Roslyn analyzer package for incremental IDE updates
- [ ] ASP.NET endpoint → handler → repository flow templates
- [x] MediatR specialized edges (`dispatches`, `handles`, `publishes`)
- [ ] JSON export compatible with Graphify tooling
- [ ] Watch mode to rebuild graph on file changes

### License

MIT
