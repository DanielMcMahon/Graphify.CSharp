using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace Graphify.CSharp.Web;

public static class GraphWebHost
{
    public static async Task RunAsync(string[] args)
    {
        var options = GraphWebOptions.Parse(args);
        var contentRoot = Path.GetDirectoryName(typeof(GraphWebHost).Assembly.Location)
            ?? AppContext.BaseDirectory;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = contentRoot
        });
        var app = builder.Build();

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/api/overview", async (bool? justMyCode) =>
        {
            await using var database = await GraphDatabase.OpenAsync(options.DatabasePath).ConfigureAwait(false);
            return Results.Ok(await database.GetOverviewAsync(justMyCode: justMyCode == true).ConfigureAwait(false));
        });

        app.MapGet("/api/search", async (string? q, int? limit, bool? justMyCode) =>
        {
            await using var database = await GraphDatabase.OpenAsync(options.DatabasePath).ConfigureAwait(false);
            var nodes = string.IsNullOrWhiteSpace(q)
                ? (await database.GetOverviewAsync(justMyCode: justMyCode == true).ConfigureAwait(false)).SeedNodes
                : await database.SearchNodesAsync(q, limit ?? 25, justMyCode == true).ConfigureAwait(false);
            return Results.Ok(nodes);
        });

        app.MapGet("/api/graph", async (string? center, int? depth, int? maxNodes, string? relations, bool? justMyCode) =>
        {
            await using var database = await GraphDatabase.OpenAsync(options.DatabasePath).ConfigureAwait(false);
            var relationList = string.IsNullOrWhiteSpace(relations)
                ? null
                : relations.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var export = await database.GetSubgraphAsync(
                center,
                depth ?? 2,
                maxNodes ?? 300,
                relationList,
                justMyCode == true).ConfigureAwait(false);

            return Results.Ok(ToGraphResponse(export));
        });

        app.MapGet("/api/nodes/{id}", async (string id, bool? justMyCode) =>
        {
            await using var database = await GraphDatabase.OpenAsync(options.DatabasePath).ConfigureAwait(false);
            try
            {
                return Results.Ok(await database.GetNodeDetailAsync(id, justMyCode == true).ConfigureAwait(false));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        var url = $"http://{options.Host}:{options.Port}";
        Console.WriteLine($"Graphify.CSharp UI running at {url}");
        Console.WriteLine($"Database: {Path.GetFullPath(options.DatabasePath)}");
        await app.RunAsync(url).ConfigureAwait(false);
    }

    private static object ToGraphResponse(GraphExport export) => new
    {
        metadata = export.Metadata,
        nodes = export.Nodes.Select(node => new
        {
            id = node.Id,
            label = node.Name,
            title = BuildNodeTitle(node),
            kind = node.Kind.ToString(),
            fullName = node.FullName,
            assembly = node.Assembly,
            filePath = node.FilePath,
            line = node.Line
        }),
        edges = export.Edges.Select(edge => new
        {
            id = $"{edge.SourceId}->{edge.TargetId}:{edge.Relation}",
            from = edge.SourceId,
            to = edge.TargetId,
            label = edge.Relation,
            relation = edge.Relation,
            confidence = edge.Confidence.ToString(),
            title = $"{edge.Relation} ({edge.Confidence})",
            sourceFile = edge.SourceFile,
            line = edge.Line
        })
    };

    private static string BuildNodeTitle(GraphNode node)
    {
        var location = string.IsNullOrEmpty(node.FilePath) ? string.Empty : $"\n{node.FilePath}:{node.Line}";
        return $"{node.FullName ?? node.Name}\n{node.Kind}{location}";
    }
}

public sealed record GraphWebOptions(string DatabasePath, string Host, int Port)
{
    public static GraphWebOptions Parse(string[] args)
    {
        var databasePath = ".graphify/graph.db";
        var host = "127.0.0.1";
        var port = 5173;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--db" when i + 1 < args.Length:
                    databasePath = args[++i];
                    break;
                case "--host" when i + 1 < args.Length:
                    host = args[++i];
                    break;
                case "--port" when i + 1 < args.Length:
                    port = int.Parse(args[++i]);
                    break;
            }
        }

        return new GraphWebOptions(databasePath, host, port);
    }
}
