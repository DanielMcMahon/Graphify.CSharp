using Graphify.CSharp.Core;
using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Query;
using Graphify.CSharp.Core.Storage;
using Graphify.CSharp.Core.Workspace;
using Graphify.CSharp.Roslyn;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;

namespace Graphify.CSharp.Mcp;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.AddConsole(consoleLogOptions =>
        {
            consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services
            .AddSingleton<GraphQueryService>()
            .AddSingleton<GraphFlowService>()
            .AddSingleton<HowDoesItWorkService>()
            .AddSingleton<InvestigationService>()
            .AddSingleton<ArchitectureAssessmentService>()
            .AddSingleton<TableMigrationTraceService>()
            .AddSingleton<UiAccessPathService>()
            .AddSingleton<GraphReportGenerator>()
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync().ConfigureAwait(false);
    }
}

[McpServerToolType]
public static class GraphTools
{
    [McpServerTool, Description("Trace backwards from a database table to entities, SQL query sites, ASPX pages, file-path columns, and file storage touchpoints. Use for local-file-to-blob migration planning.")]
    public static async Task<string> TraceTable(
        TableMigrationTraceService traceService,
        [Description("Database table name, e.g. Documents or Attachments")] string tableName,
        [Description("Optional project root containing .graphify/config.json")] string? projectRoot = null,
        CancellationToken cancellationToken = default)
    {
        var ensureResult = await EnsureGraphInternalAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        await using var database = await GraphDatabase.OpenAsync(ensureResult.DatabasePath, cancellationToken).ConfigureAwait(false);
        var result = await traceService.TraceAsync(database, tableName, cancellationToken).ConfigureAwait(false);
        return ensureResult.WasAlreadyFresh
            ? result.Markdown
            : ensureResult.Message + Environment.NewLine + Environment.NewLine + result.Markdown;
    }

    [McpServerTool, Description("Get prerequisites, visibility gates, selector hints, and navigation steps for reaching a UI element on a server-rendered surface. Use before writing Playwright tests.")]
    public static async Task<string> GetUiAccessPath(
        UiAccessPathService uiService,
        [Description("UI element, fragment, label, or selector query")] string element,
        [Description("Optional UI surface or page name to narrow the search")] string? surface = null,
        [Description("Optional project root containing .graphify/config.json")] string? projectRoot = null,
        [Description("Return machine-readable JSON instead of markdown")] bool json = false,
        CancellationToken cancellationToken = default)
    {
        var ensureResult = await EnsureGraphInternalAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        await using var database = await GraphDatabase.OpenAsync(ensureResult.DatabasePath, cancellationToken).ConfigureAwait(false);
        var result = await uiService.GetAccessPathAsync(database, element, surface, cancellationToken).ConfigureAwait(false);
        var output = json ? result.Json : result.Markdown;
        return ensureResult.WasAlreadyFresh
            ? output
            : ensureResult.Message + Environment.NewLine + Environment.NewLine + output;
    }

    [McpServerTool, Description("List fragments, elements, gates, and actions rendered by a UI surface or page.")]
    public static async Task<string> ListSurfaceUi(
        UiAccessPathService uiService,
        [Description("UI surface or page name")] string surface,
        [Description("Optional project root containing .graphify/config.json")] string? projectRoot = null,
        CancellationToken cancellationToken = default)
    {
        var ensureResult = await EnsureGraphInternalAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        await using var database = await GraphDatabase.OpenAsync(ensureResult.DatabasePath, cancellationToken).ConfigureAwait(false);
        var result = await uiService.GetSurfaceMapAsync(database, surface, cancellationToken).ConfigureAwait(false);
        return ensureResult.WasAlreadyFresh
            ? result.Markdown
            : ensureResult.Message + Environment.NewLine + Environment.NewLine + result.Markdown;
    }

    [McpServerTool, Description("Export Playwright-oriented UI prerequisites JSON for a UI element.")]
    public static async Task<string> ExportUiPrerequisites(
        UiAccessPathService uiService,
        [Description("UI element, fragment, or label query")] string element,
        [Description("Optional UI surface or page name")] string? surface = null,
        [Description("Optional project root containing .graphify/config.json")] string? projectRoot = null,
        CancellationToken cancellationToken = default)
    {
        var ensureResult = await EnsureGraphInternalAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        await using var database = await GraphDatabase.OpenAsync(ensureResult.DatabasePath, cancellationToken).ConfigureAwait(false);
        var json = await uiService.ExportPrerequisitesJsonAsync(database, element, surface, cancellationToken).ConfigureAwait(false);
        return ensureResult.WasAlreadyFresh
            ? json
            : ensureResult.Message + Environment.NewLine + Environment.NewLine + json;
    }

    [McpServerTool, Description("Run a full architecture investigation for any question or feature name. Returns summary, explanation, impact analysis, files to read, and writes a handoff markdown file. Use this as the default entry point for open-ended questions.")]
    public static async Task<string> Investigate(
        InvestigationService investigationService,
        [Description("Natural language question or symbol, e.g. 'how does OfferJob work?' or 'INotifier'")] string question,
        [Description("Optional project root containing .graphify/config.json")] string? projectRoot = null,
        [Description("Write .graphify/investigations/<topic>.md handoff file for other agents")] bool writeHandoff = true,
        CancellationToken cancellationToken = default)
    {
        var ensureResult = await EnsureGraphInternalAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        projectRoot = ResolveProjectRoot(projectRoot);
        await using var database = await GraphDatabase.OpenAsync(ensureResult.DatabasePath, cancellationToken).ConfigureAwait(false);
        var result = await investigationService.InvestigateAsync(database, question, projectRoot, writeHandoff, cancellationToken).ConfigureAwait(false);

        if (!ensureResult.WasAlreadyFresh)
        {
            return ensureResult.Message + Environment.NewLine + Environment.NewLine + FormatInvestigation(result);
        }

        return FormatInvestigation(result);
    }

    [McpServerTool, Description("Assess how hard an architectural change would be: remove or replace a symbol, swap MediatR for direct handler calls, estimate blast radius and migration steps.")]
    public static async Task<string> AssessChange(
        ArchitectureAssessmentService assessmentService,
        [Description("Symbol to remove/replace, or 'mediator' / 'mediatr' for a MediatR swap assessment")] string topic,
        [Description("Optional project root containing .graphify/config.json")] string? projectRoot = null,
        CancellationToken cancellationToken = default)
    {
        var ensureResult = await EnsureGraphInternalAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        await using var database = await GraphDatabase.OpenAsync(ensureResult.DatabasePath, cancellationToken).ConfigureAwait(false);

        string markdown;
        if (assessmentService.LooksLikeMediatorQuestion(topic))
        {
            markdown = (await assessmentService.AssessMediatorReplacementAsync(database, cancellationToken: cancellationToken).ConfigureAwait(false)).Markdown;
        }
        else
        {
            var matches = await database.SearchNodesAsync(topic, limit: 1, justMyCode: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (matches.Count == 0)
            {
                markdown = $"No symbol matched '{topic}'. Try a type/interface name, or 'mediator' for a MediatR swap assessment.";
            }
            else
            {
                markdown = (await assessmentService.AssessSymbolRemovalAsync(database, matches[0], cancellationToken).ConfigureAwait(false)).Markdown;
            }
        }

        return ensureResult.WasAlreadyFresh
            ? markdown
            : ensureResult.Message + Environment.NewLine + Environment.NewLine + markdown;
    }

    [McpServerTool, Description("Answer 'how does X work?' for a C# symbol, feature, handler, service, or endpoint. Builds or refreshes the graph automatically when needed.")]
    public static async Task<string> HowDoesItWork(
        HowDoesItWorkService howService,
        [Description("Symbol, class, method, or feature name such as OfferJob, INotifier, or JobsController")] string topic,
        [Description("Optional project root containing .graphify/config.json")] string? projectRoot = null,
        CancellationToken cancellationToken = default)
    {
        var ensureResult = await EnsureGraphInternalAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        var databasePath = ensureResult.DatabasePath;
        await using var database = await GraphDatabase.OpenAsync(databasePath, cancellationToken).ConfigureAwait(false);
        var explanation = await howService.ExplainAsync(database, topic, cancellationToken).ConfigureAwait(false);
        if (!ensureResult.WasAlreadyFresh)
        {
            return ensureResult.Message + Environment.NewLine + Environment.NewLine + explanation;
        }

        return explanation;
    }

    [McpServerTool, Description("Ensure the knowledge graph exists and is up to date for the current project.")]
    public static async Task<string> EnsureGraph(
        [Description("Optional project root containing .graphify/config.json")] string? projectRoot = null,
        CancellationToken cancellationToken = default)
    {
        var result = await EnsureGraphInternalAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        return result.Message;
    }

    [McpServerTool, Description("Report whether the graph exists, when it was built, and whether it is stale.")]
    public static async Task<string> GetGraphStatus(
        [Description("Optional project root containing .graphify/config.json")] string? projectRoot = null,
        CancellationToken cancellationToken = default)
    {
        projectRoot = ResolveProjectRoot(projectRoot);
        var status = await GraphWorkspace.GetStatusAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        var builder = new StringBuilder();
        builder.AppendLine($"Project root: {projectRoot}");
        builder.AppendLine($"Database: {status.DatabasePath}");
        builder.AppendLine($"Database exists: {status.DatabaseExists}");
        builder.AppendLine($"Configured: {status.ConfigExists}");
        builder.AppendLine($"Stale: {status.IsStale}");
        builder.AppendLine($"Solution: {status.SolutionPath ?? "not found"}");
        builder.AppendLine($"Built at: {status.BuiltAt?.ToString("O") ?? "unknown"}");
        builder.AppendLine($"Nodes: {status.NodeCount:N0}");
        builder.AppendLine($"Edges: {status.EdgeCount:N0}");
        if (!string.IsNullOrWhiteSpace(status.NewestSourceChange))
        {
            builder.AppendLine($"Newest source change: {status.NewestSourceChange}");
        }

        return builder.ToString();
    }

    [McpServerTool, Description("Build a Roslyn knowledge graph from a C# solution or project.")]
    public static async Task<string> BuildGraph(
        [Description("Path to a .sln or .csproj file")] string path,
        [Description("SQLite output path (defaults to GRAPHIFY_DB or .graphify/graph.db)")] string? output = null,
        CancellationToken cancellationToken = default)
    {
        output = GraphifyEnvironment.ResolveDatabasePath(output);
        var builder = new RoslynGraphBuilder();
        var snapshot = await builder.BuildAsync(path, cancellationToken).ConfigureAwait(false);
        await using var database = await GraphDatabase.OpenAsync(output, cancellationToken).ConfigureAwait(false);
        await database.ReplaceSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return $"Built graph with {snapshot.Nodes.Count} nodes and {snapshot.Edges.Count} edges at {output}";
    }

    [McpServerTool, Description("Search the knowledge graph for symbols by name or fully-qualified type.")]
    public static async Task<string> QuerySymbol(
        GraphQueryService queryService,
        [Description("Search text")] string query,
        [Description("SQLite database path (defaults to GRAPHIFY_DB or .graphify/graph.db)")] string? databasePath = null,
        CancellationToken cancellationToken = default)
    {
        databasePath = await ResolveDatabasePathAsync(databasePath, cancellationToken).ConfigureAwait(false);
        await using var database = await GraphDatabase.OpenAsync(databasePath, cancellationToken).ConfigureAwait(false);
        var nodes = await database.SearchNodesAsync(query, cancellationToken: cancellationToken).ConfigureAwait(false);
        return FormatNodes(nodes);
    }

    [McpServerTool, Description("Find knowledge paths between two C# symbols.")]
    public static async Task<string> FindPath(
        GraphQueryService queryService,
        [Description("Source symbol query")] string from,
        [Description("Target symbol query")] string to,
        [Description("SQLite database path (defaults to GRAPHIFY_DB or .graphify/graph.db)")] string? databasePath = null,
        CancellationToken cancellationToken = default)
    {
        databasePath = await ResolveDatabasePathAsync(databasePath, cancellationToken).ConfigureAwait(false);
        await using var database = await GraphDatabase.OpenAsync(databasePath, cancellationToken).ConfigureAwait(false);
        var paths = await queryService.FindPathsAsync(database, from, to, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (paths.Count == 0)
        {
            return "No path found.";
        }

        var builder = new StringBuilder();
        var index = 1;
        foreach (var path in paths)
        {
            builder.AppendLine($"Path {index++}:");
            foreach (var step in path.Steps)
            {
                if (step.IncomingEdge is null)
                {
                    builder.AppendLine($"  {step.Node.FullName ?? step.Node.Name}");
                    continue;
                }

                builder.AppendLine($"  --[{step.IncomingEdge.Relation}/{step.IncomingEdge.Confidence}]--> {step.Node.FullName ?? step.Node.Name}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    [McpServerTool, Description("Explain incoming and outgoing relationships for a symbol.")]
    public static async Task<string> ExplainSymbol(
        GraphQueryService queryService,
        [Description("Symbol query")] string symbol,
        [Description("SQLite database path (defaults to GRAPHIFY_DB or .graphify/graph.db)")] string? databasePath = null,
        CancellationToken cancellationToken = default)
    {
        databasePath = await ResolveDatabasePathAsync(databasePath, cancellationToken).ConfigureAwait(false);
        await using var database = await GraphDatabase.OpenAsync(databasePath, cancellationToken).ConfigureAwait(false);
        var explanation = await queryService.ExplainAsync(database, symbol, cancellationToken).ConfigureAwait(false);
        if (explanation.Node is null)
        {
            return "Symbol not found.";
        }

        var builder = new StringBuilder();
        builder.AppendLine(explanation.Node.FullName ?? explanation.Node.Name);
        builder.AppendLine("Incoming:");
        foreach (var edge in explanation.Incoming)
        {
            var source = await database.GetNodeAsync(edge.SourceId, cancellationToken).ConfigureAwait(false);
            builder.AppendLine($"  {edge.Relation} <- {source?.FullName ?? edge.SourceId} ({edge.Confidence})");
        }

        builder.AppendLine("Outgoing:");
        foreach (var edge in explanation.Outgoing)
        {
            var target = await database.GetNodeAsync(edge.TargetId, cancellationToken).ConfigureAwait(false);
            builder.AppendLine($"  {edge.Relation} -> {target?.FullName ?? edge.TargetId} ({edge.Confidence})");
        }

        return builder.ToString();
    }

    [McpServerTool, Description("Trace ASP.NET endpoint or handler flows toward repositories.")]
    public static async Task<string> FindFlows(
        GraphFlowService flowService,
        [Description("Endpoint route, controller, or handler query")] string endpoint,
        [Description("SQLite database path (defaults to GRAPHIFY_DB or .graphify/graph.db)")] string? databasePath = null,
        CancellationToken cancellationToken = default)
    {
        databasePath = await ResolveDatabasePathAsync(databasePath, cancellationToken).ConfigureAwait(false);
        await using var database = await GraphDatabase.OpenAsync(databasePath, cancellationToken).ConfigureAwait(false);
        var paths = await flowService.FindEndpointFlowsAsync(database, endpoint, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (paths.Count == 0)
        {
            return "No endpoint flow found.";
        }

        var builder = new StringBuilder();
        var index = 1;
        foreach (var path in paths)
        {
            builder.AppendLine($"Flow {index++}:");
            foreach (var step in path.Steps)
            {
                if (step.IncomingEdge is null)
                {
                    builder.AppendLine($"  {step.Node.FullName ?? step.Node.Name}");
                    continue;
                }

                builder.AppendLine($"  --[{step.IncomingEdge.Relation}/{step.IncomingEdge.Confidence}]--> {step.Node.FullName ?? step.Node.Name}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    [McpServerTool, Description("Suggest likely architectural gaps when no path exists between symbols.")]
    public static async Task<string> FindGaps(
        GraphQueryService queryService,
        [Description("Source symbol query")] string from,
        [Description("Target symbol query")] string to,
        [Description("SQLite database path (defaults to GRAPHIFY_DB or .graphify/graph.db)")] string? databasePath = null,
        CancellationToken cancellationToken = default)
    {
        databasePath = await ResolveDatabasePathAsync(databasePath, cancellationToken).ConfigureAwait(false);
        await using var database = await GraphDatabase.OpenAsync(databasePath, cancellationToken).ConfigureAwait(false);
        var lines = await queryService.FindGapsAsync(database, from, to, cancellationToken).ConfigureAwait(false);
        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<EnsureGraphResult> EnsureGraphInternalAsync(string? projectRoot, CancellationToken cancellationToken)
    {
        projectRoot = ResolveProjectRoot(projectRoot);
        var config = GraphWorkspace.LoadConfig(projectRoot);
        var solutionPath = GraphifyEnvironment.ResolveSolutionPath(config?.SolutionPath) ?? GraphWorkspace.DiscoverSolutionPath(projectRoot);
        var databasePath = config?.DatabasePath ?? GraphWorkspace.GetDefaultDatabasePath(projectRoot);
        if (!Path.IsPathRooted(databasePath))
        {
            databasePath = Path.Combine(projectRoot, databasePath);
        }

        var status = await GraphWorkspace.GetStatusAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        if (status.DatabaseExists && !status.IsStale)
        {
            return new EnsureGraphResult(databasePath, true, $"Graph is up to date ({status.NodeCount:N0} nodes).");
        }

        if (string.IsNullOrWhiteSpace(solutionPath) || !File.Exists(solutionPath))
        {
            return new EnsureGraphResult(databasePath, false, "No solution found. Run `graphify-csharp init` in the project root first.");
        }

        if (config is null)
        {
            GraphWorkspace.CreateConfig(projectRoot, solutionPath, databasePath, GraphWorkspace.GetDefaultJsonPath(projectRoot));
        }

        var builder = new RoslynGraphBuilder();
        var snapshot = await builder.BuildAsync(solutionPath, cancellationToken).ConfigureAwait(false);
        await using var database = await GraphDatabase.OpenAsync(databasePath, cancellationToken).ConfigureAwait(false);
        await database.ReplaceSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);

        var reportGenerator = new GraphReportGenerator();
        await reportGenerator.WriteAsync(projectRoot, database, cancellationToken).ConfigureAwait(false);

        var message = status.DatabaseExists
            ? $"Rebuilt stale graph with {snapshot.Nodes.Count:N0} nodes and {snapshot.Edges.Count:N0} edges."
            : $"Built graph with {snapshot.Nodes.Count:N0} nodes and {snapshot.Edges.Count:N0} edges.";

        return new EnsureGraphResult(databasePath, false, message);
    }

    private static string ResolveProjectRoot(string? projectRoot)
    {
        projectRoot = GraphifyEnvironment.ResolveProjectRoot(projectRoot);
        return GraphWorkspace.DiscoverProjectRoot(projectRoot) ?? projectRoot;
    }

    private static async Task<string> ResolveDatabasePathAsync(string? databasePath, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(databasePath))
        {
            return databasePath;
        }

        var resolved = GraphifyEnvironment.ResolveDatabasePath();
        if (Path.IsPathRooted(resolved))
        {
            return resolved;
        }

        var projectRoot = ResolveProjectRoot(null);
        var config = GraphWorkspace.LoadConfig(projectRoot);
        if (config is not null)
        {
            return config.DatabasePath;
        }

        var status = await GraphWorkspace.GetStatusAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        return status.DatabasePath;
    }

    private static string FormatNodes(IReadOnlyList<GraphNode> nodes)
    {
        if (nodes.Count == 0)
        {
            return "No matches.";
        }

        var builder = new StringBuilder();
        foreach (var node in nodes)
        {
            builder.AppendLine($"{node.Kind}: {node.FullName ?? node.Name}");
            if (!string.IsNullOrEmpty(node.FilePath))
            {
                builder.AppendLine($"  {node.FilePath}:{node.Line}");
            }
        }

        return builder.ToString();
    }

    private static string FormatInvestigation(InvestigationResult result)
    {
        var builder = new StringBuilder(result.Markdown);
        if (!string.IsNullOrWhiteSpace(result.HandoffPath))
        {
            builder.AppendLine($"Handoff written to: {result.HandoffPath}");
        }

        return builder.ToString();
    }

    private sealed record EnsureGraphResult(string DatabasePath, bool WasAlreadyFresh, string Message);
}
