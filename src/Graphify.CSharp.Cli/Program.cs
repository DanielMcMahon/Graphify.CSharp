using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Query;
using Graphify.CSharp.Core.Storage;
using Graphify.CSharp.Roslyn;
using Graphify.CSharp.Web;
using System.CommandLine;

namespace Graphify.CSharp.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var root = new RootCommand("Build and query a Roslyn-powered C# knowledge graph.");

        var buildCommand = new Command("build", "Analyze a solution or project and persist the knowledge graph.");
        var buildPathArg = new Argument<string>("path", "Path to a .sln or .csproj file.");
        var buildOutputOption = new Option<string>("--output", () => ".graphify/graph.db", "SQLite database output path.");
        buildCommand.AddArgument(buildPathArg);
        buildCommand.AddOption(buildOutputOption);
        buildCommand.SetHandler(BuildAsync, buildPathArg, buildOutputOption);
        root.AddCommand(buildCommand);

        var queryCommand = new Command("query", "Search graph nodes by name or fully-qualified symbol.");
        var queryDbOption = new Option<string>("--db", () => ".graphify/graph.db", "SQLite database path.");
        var queryTextArg = new Argument<string>("text", "Search text.");
        queryCommand.AddOption(queryDbOption);
        queryCommand.AddArgument(queryTextArg);
        queryCommand.SetHandler(QueryAsync, queryDbOption, queryTextArg);
        root.AddCommand(queryCommand);

        var pathCommand = new Command("path", "Find knowledge paths between two symbols.");
        var pathDbOption = new Option<string>("--db", () => ".graphify/graph.db", "SQLite database path.");
        var fromArg = new Argument<string>("from", "Source symbol query.");
        var toArg = new Argument<string>("to", "Target symbol query.");
        pathCommand.AddOption(pathDbOption);
        pathCommand.AddArgument(fromArg);
        pathCommand.AddArgument(toArg);
        pathCommand.SetHandler(PathAsync, pathDbOption, fromArg, toArg);
        root.AddCommand(pathCommand);

        var explainCommand = new Command("explain", "Show incoming and outgoing relationships for a symbol.");
        var explainDbOption = new Option<string>("--db", () => ".graphify/graph.db", "SQLite database path.");
        var explainArg = new Argument<string>("symbol", "Symbol query.");
        explainCommand.AddOption(explainDbOption);
        explainCommand.AddArgument(explainArg);
        explainCommand.SetHandler(ExplainAsync, explainDbOption, explainArg);
        root.AddCommand(explainCommand);

        var gapsCommand = new Command("gaps", "Suggest likely gaps when no path exists between symbols.");
        var gapsDbOption = new Option<string>("--db", () => ".graphify/graph.db", "SQLite database path.");
        var gapsFromArg = new Argument<string>("from", "Source symbol query.");
        var gapsToArg = new Argument<string>("to", "Target symbol query.");
        gapsCommand.AddOption(gapsDbOption);
        gapsCommand.AddArgument(gapsFromArg);
        gapsCommand.AddArgument(gapsToArg);
        gapsCommand.SetHandler(GapsAsync, gapsDbOption, gapsFromArg, gapsToArg);
        root.AddCommand(gapsCommand);

        var serveCommand = new Command("serve", "Launch the interactive graph UI in your browser.");
        var serveDbOption = new Option<string>("--db", () => ".graphify/graph.db", "SQLite database path.");
        var servePortOption = new Option<int>("--port", () => 5173, "HTTP port.");
        var serveHostOption = new Option<string>("--host", () => "127.0.0.1", "HTTP host.");
        serveCommand.AddOption(serveDbOption);
        serveCommand.AddOption(servePortOption);
        serveCommand.AddOption(serveHostOption);
        serveCommand.SetHandler(ServeAsync, serveDbOption, servePortOption, serveHostOption);
        root.AddCommand(serveCommand);

        return await root.InvokeAsync(args).ConfigureAwait(false);
    }

    private static async Task BuildAsync(string path, string output)
    {
        Console.WriteLine($"Building graph from {path}...");
        var builder = new RoslynGraphBuilder();
        var snapshot = await builder.BuildAsync(path).ConfigureAwait(false);

        await using var database = await GraphDatabase.OpenAsync(output).ConfigureAwait(false);
        await database.ReplaceSnapshotAsync(snapshot).ConfigureAwait(false);

        Console.WriteLine($"Wrote {snapshot.Nodes.Count} nodes and {snapshot.Edges.Count} edges to {output}");
    }

    private static async Task QueryAsync(string dbPath, string text)
    {
        await using var database = await GraphDatabase.OpenAsync(dbPath).ConfigureAwait(false);
        var nodes = await database.SearchNodesAsync(text).ConfigureAwait(false);

        foreach (var node in nodes)
        {
            Console.WriteLine($"{node.Kind,-10} {node.FullName ?? node.Name}");
            if (!string.IsNullOrEmpty(node.FilePath))
            {
                Console.WriteLine($"           {node.FilePath}:{node.Line}");
            }
        }
    }

    private static async Task PathAsync(string dbPath, string from, string to)
    {
        await using var database = await GraphDatabase.OpenAsync(dbPath).ConfigureAwait(false);
        var service = new GraphQueryService();
        var paths = await service.FindPathsAsync(database, from, to).ConfigureAwait(false);

        if (paths.Count == 0)
        {
            Console.WriteLine("No path found.");
            return;
        }

        var index = 1;
        foreach (var path in paths)
        {
            Console.WriteLine($"Path {index++}:");
            foreach (var step in path.Steps)
            {
                if (step.IncomingEdge is null)
                {
                    Console.WriteLine($"  {step.Node.FullName ?? step.Node.Name}");
                    continue;
                }

                Console.WriteLine($"  --[{step.IncomingEdge.Relation}/{step.IncomingEdge.Confidence}]--> {step.Node.FullName ?? step.Node.Name}");
                if (!string.IsNullOrEmpty(step.IncomingEdge.SourceFile))
                {
                    Console.WriteLine($"      at {step.IncomingEdge.SourceFile}:{step.IncomingEdge.Line}");
                }
            }

            Console.WriteLine();
        }
    }

    private static async Task ExplainAsync(string dbPath, string symbol)
    {
        await using var database = await GraphDatabase.OpenAsync(dbPath).ConfigureAwait(false);
        var service = new GraphQueryService();
        var explanation = await service.ExplainAsync(database, symbol).ConfigureAwait(false);

        if (explanation.Node is null)
        {
            Console.WriteLine("Symbol not found.");
            return;
        }

        Console.WriteLine(explanation.Node.FullName ?? explanation.Node.Name);
        Console.WriteLine("Incoming:");
        foreach (var edge in explanation.Incoming)
        {
            var source = await database.GetNodeAsync(edge.SourceId).ConfigureAwait(false);
            Console.WriteLine($"  {edge.Relation} <- {source?.FullName ?? edge.SourceId} ({edge.Confidence})");
        }

        Console.WriteLine("Outgoing:");
        foreach (var edge in explanation.Outgoing)
        {
            var target = await database.GetNodeAsync(edge.TargetId).ConfigureAwait(false);
            Console.WriteLine($"  {edge.Relation} -> {target?.FullName ?? edge.TargetId} ({edge.Confidence})");
        }
    }

    private static async Task GapsAsync(string dbPath, string from, string to)
    {
        await using var database = await GraphDatabase.OpenAsync(dbPath).ConfigureAwait(false);
        var service = new GraphQueryService();
        var lines = await service.FindGapsAsync(database, from, to).ConfigureAwait(false);
        foreach (var line in lines)
        {
            Console.WriteLine(line);
        }
    }

    private static Task ServeAsync(string dbPath, int port, string host) =>
        GraphWebHost.RunAsync(["--db", dbPath, "--port", port.ToString(), "--host", host]);
}
