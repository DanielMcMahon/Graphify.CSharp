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

### MCP server (Cursor)

Add to your MCP config:

```json
{
  "mcpServers": {
    "graphify-csharp": {
      "command": "dotnet",
      "args": ["run", "--project", "/absolute/path/to/Graphify.CSharp/src/Graphify.CSharp.Mcp"]
    }
  }
}
```

Tools: `BuildGraph`, `QuerySymbol`, `FindPath`, `ExplainSymbol`, `FindGaps`.

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

Confidence: `Extracted` (compiler-resolved), `Ambiguous` (heuristic/unresolved), `Inferred` (reserved for future doc linking).

### Roadmap

- [ ] Roslyn analyzer package for incremental IDE updates
- [ ] ASP.NET endpoint → handler → repository flow templates
- [ ] MediatR / EF Core / DI registration specialized edges
- [ ] JSON export compatible with Graphify tooling
- [ ] Watch mode to rebuild graph on file changes

### License

MIT
