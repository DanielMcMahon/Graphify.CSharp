---
name: graphify-csharp
description: >-
  Answer C# architecture questions from a Roslyn knowledge graph. Use when the user
  asks how something works, who calls what, how data flows, or how features are wired.
  Automatically builds/refreshes the graph and queries via MCP tools.
---

# Graphify.CSharp Agent Skill

You have access to a **Roslyn-powered C# knowledge graph** for this project. Use it instead of guessing architecture.

## Golden rule

When the user asks **"how does X work?"**, **"who calls Y?"**, **"trace Z"**, or any architecture question:

1. Call **`Investigate`** MCP tool first — it runs the full pipeline (search, explain, impact, files, handoff).
2. If you already know the exact symbol and only need edges, use **`HowDoesItWork`** or **`ExplainSymbol`**.
3. If tools fail because the graph is missing, call **`EnsureGraph`** first, then retry.
4. Answer in plain language using the graph results — include callers, dependencies, MediatR wiring, and file paths.

Do not guess when the graph can answer.

## MCP tools (preferred)

| Tool | When to use |
|------|-------------|
| `Investigate` | **Default** — any open question ("how does job offering work?", "can we remove INotifier?") |
| `AssessChange` | **Refactoring** — removal difficulty, MediatR swap, blast radius |
| `TraceTable` | **Migration** — start from a DB table, trace back to pages, file columns, storage |
| `GetUiAccessPath` | **Playwright / UI** — prerequisites and selector hints for a UI element |
| `ListSurfaceUi` | **Playwright / UI** — map fragments, elements, gates on a surface |
| `ExportUiPrerequisites` | **Playwright / UI** — JSON prerequisites for test setup |
| `HowDoesItWork` | Known symbol, focused explanation |
| `EnsureGraph` | Graph missing, stale, or first query in session |
| `GetGraphStatus` | Check if graph exists and is current |
| `QuerySymbol` | Search for symbols by name |
| `ExplainSymbol` | Detailed incoming/outgoing edges |
| `FindPath` | Trace connection between two symbols |
| `FindFlows` | API endpoint → handler → repository |
| `FindGaps` | No path exists — suggest missing links |
| `BuildGraph` | Force full rebuild |

## Automatic graph management

The graph lives at `.graphify/graph.db`. The MCP server:

- Auto-discovers the project root and solution
- Rebuilds when the graph is missing or stale
- Writes `.graphify/GRAPH_REPORT.md` with hub symbols and suggested questions

You should **not** ask the user to run CLI commands unless MCP is unavailable.

## Example user questions → tool calls

| User says | You do |
|-----------|--------|
| "How does job offering work?" | `Investigate("how does job offering work?")` |
| "Migrate Documents table from file storage to blob" | `TraceTable("Documents")` |
| "Who calls INotifier?" | `Investigate("who calls INotifier")` |
| "What if we removed MediatR?" | `AssessChange("mediator")` or `Investigate("swap mediatr for direct handler calls")` |
| "Can I remove ShiftService?" | `AssessChange("ShiftService")` |
| "Trace from JobsController to the database" | `FindFlows("JobsController")` then `FindPath` if needed |
| "How do I reach the invoice link on order details?" | `GetUiAccessPath("invoice-link", surface: "OrderDetailsPage")` |
| "What UI is on OrderDetailsPage?" | `ListSurfaceUi("OrderDetailsPage")` |

## Answering format

Structure answers as:

1. **What it is** — type, assembly, file location
2. **How it's triggered** — callers, routes, MediatR dispatches
3. **What it does next** — outgoing calls, handlers, repositories
4. **Key files** — source paths from the graph

Prefer **Extracted** edges over **Ambiguous** when explaining flows.

## CLI fallback (only if MCP unavailable)

```bash
graphify-csharp init
graphify-csharp investigate "how does OfferJob work?"
graphify-csharp status
```

## One-time project setup

If `.graphify/config.json` does not exist, tell the user to run once:

```bash
graphify-csharp init
```

Or run it yourself via shell if available. After that, MCP handles everything automatically.
