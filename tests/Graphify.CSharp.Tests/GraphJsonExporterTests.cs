using Graphify.CSharp.Core.Export;
using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Storage;

namespace Graphify.CSharp.Tests;

public sealed class GraphJsonExporterTests
{
    [Fact]
    public void ToJson_uses_graphify_compatible_node_link_format()
    {
        var snapshot = new GraphSnapshot
        {
            SolutionPath = "/tmp/Sample.sln",
            BuiltAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            Nodes =
            [
                new GraphNode("a", NodeKind.Type, "OrderService", "OrderService", "Sample", "OrderService.cs", 1, 10)
            ],
            Edges =
            [
                new GraphEdge("a", "b", GraphRelation.Calls, GraphConfidence.Extracted, "OrderService.cs", 5)
            ]
        };

        var document = GraphJsonExporter.ToDocument(snapshot);

        Assert.True(document.Directed);
        Assert.Single(document.Nodes);
        Assert.Equal("code", document.Nodes[0].FileType);
        Assert.Single(document.Links);
        Assert.Equal("calls", document.Links[0].Relation);
        Assert.Equal("EXTRACTED", document.Links[0].Confidence);
        Assert.Equal(document.Links, document.Edges);
    }

    [Fact]
    public async Task Export_round_trips_through_database()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"graphify-export-{Guid.NewGuid():N}.db");
        var jsonPath = Path.Combine(Path.GetTempPath(), $"graphify-export-{Guid.NewGuid():N}.json");
        var snapshot = new GraphSnapshot
        {
            SolutionPath = "test.sln",
            BuiltAt = DateTimeOffset.UtcNow,
            Nodes = [new GraphNode("repo", NodeKind.Type, "IOrderRepository", "IOrderRepository", "Sample", null, null, null)],
            Edges = []
        };

        await using (var database = await GraphDatabase.OpenAsync(databasePath))
        {
            await database.ReplaceSnapshotAsync(snapshot);
        }

        await using (var database = await GraphDatabase.OpenAsync(databasePath))
        {
            var loaded = await database.LoadSnapshotAsync();
            await GraphJsonExporter.WriteAsync(loaded, jsonPath);
        }

        Assert.True(File.Exists(jsonPath));
        var json = await File.ReadAllTextAsync(jsonPath);
        Assert.Contains("\"links\"", json, StringComparison.Ordinal);
        Assert.Contains("IOrderRepository", json, StringComparison.Ordinal);
    }
}
