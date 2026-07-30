using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Query;
using Graphify.CSharp.Core.Storage;
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
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync().ConfigureAwait(false);
    }
}

[McpServerToolType]
public static class GraphTools
{
    [McpServerTool, Description("Build a Roslyn knowledge graph from a C# solution or project.")]
    public static async Task<string> BuildGraph(
        [Description("Path to a .sln or .csproj file")] string path,
        [Description("SQLite output path")] string output = ".graphify/graph.db",
        CancellationToken cancellationToken = default)
    {
        var builder = new RoslynGraphBuilder();
        var snapshot = await builder.BuildAsync(path, cancellationToken).ConfigureAwait(false);
        await using var database = await GraphDatabase.OpenAsync(output, cancellationToken).ConfigureAwait(false);
        await database.ReplaceSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return $"Built graph with {snapshot.Nodes.Count} nodes and {snapshot.Edges.Count} edges at {output}";
    }

    [McpServerTool, Description("Search the knowledge graph for symbols by name or fully-qualified type.")]
    public static async Task<string> QuerySymbol(
        GraphQueryService queryService,
        [Description("SQLite database path")] string databasePath = ".graphify/graph.db",
        [Description("Search text")] string query = "",
        CancellationToken cancellationToken = default)
    {
        await using var database = await GraphDatabase.OpenAsync(databasePath, cancellationToken).ConfigureAwait(false);
        var nodes = await database.SearchNodesAsync(query, cancellationToken: cancellationToken).ConfigureAwait(false);
        return FormatNodes(nodes);
    }

    [McpServerTool, Description("Find knowledge paths between two C# symbols.")]
    public static async Task<string> FindPath(
        GraphQueryService queryService,
        [Description("SQLite database path")] string databasePath = ".graphify/graph.db",
        [Description("Source symbol query")] string from = "",
        [Description("Target symbol query")] string to = "",
        CancellationToken cancellationToken = default)
    {
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
        [Description("SQLite database path")] string databasePath = ".graphify/graph.db",
        [Description("Symbol query")] string symbol = "",
        CancellationToken cancellationToken = default)
    {
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

    [McpServerTool, Description("Suggest likely architectural gaps when no path exists between symbols.")]
    public static async Task<string> FindGaps(
        GraphQueryService queryService,
        [Description("SQLite database path")] string databasePath = ".graphify/graph.db",
        [Description("Source symbol query")] string from = "",
        [Description("Target symbol query")] string to = "",
        CancellationToken cancellationToken = default)
    {
        await using var database = await GraphDatabase.OpenAsync(databasePath, cancellationToken).ConfigureAwait(false);
        var lines = await queryService.FindGapsAsync(database, from, to, cancellationToken).ConfigureAwait(false);
        return string.Join(Environment.NewLine, lines);
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
}
