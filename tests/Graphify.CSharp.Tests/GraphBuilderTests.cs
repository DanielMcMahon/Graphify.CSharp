using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Query;
using Graphify.CSharp.Core.Storage;
using Graphify.CSharp.Roslyn;

namespace Graphify.CSharp.Tests;

public class GraphBuilderTests
{
    private static string SampleSolutionPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "SampleApp", "SampleApp.sln"));

    [Fact]
    public async Task Build_creates_call_and_inject_edges()
    {
        var solutionPath = SampleSolutionPath;
        var databasePath = Path.Combine(Path.GetTempPath(), $"graphify-test-{Guid.NewGuid():N}.db");

        var builder = new RoslynGraphBuilder();
        var snapshot = await builder.BuildAsync(solutionPath);

        await using var database = await GraphDatabase.OpenAsync(databasePath);
        await database.ReplaceSnapshotAsync(snapshot);

        var orderService = await database.SearchNodesAsync("OrderService.CreateOrderAsync");
        Assert.NotEmpty(orderService);

        var explanation = await new GraphQueryService().ExplainAsync(database, "CreateOrderAsync");
        Assert.Contains(explanation.Outgoing, edge => edge.Relation == GraphRelation.Calls);

        var typeExplanation = await new GraphQueryService().ExplainAsync(database, "OrderService");
        Assert.Contains(typeExplanation.Outgoing, edge => edge.Relation == GraphRelation.Injects);
    }

    [Fact]
    public async Task FindPaths_connects_service_to_repository()
    {
        var solutionPath = SampleSolutionPath;
        var databasePath = Path.Combine(Path.GetTempPath(), $"graphify-test-{Guid.NewGuid():N}.db");

        var builder = new RoslynGraphBuilder();
        var snapshot = await builder.BuildAsync(solutionPath);

        await using var database = await GraphDatabase.OpenAsync(databasePath);
        await database.ReplaceSnapshotAsync(snapshot);

        var paths = await new GraphQueryService().FindPathsAsync(database, "OrderService", "IOrderRepository");
        Assert.NotEmpty(paths);
    }

    [Fact]
    public async Task FindPaths_follows_reverse_handles_edges()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"graphify-test-{Guid.NewGuid():N}.db");
        var handler = new GraphNode("handler", NodeKind.Type, "OfferJobCommandHandler", "OfferJobCommandHandler", "App", null, null, null);
        var command = new GraphNode("command", NodeKind.Type, "OfferJobCommand", "OfferJobCommand", "App", null, null, null);
        var endpoint = new GraphNode("endpoint", NodeKind.Method, "OfferJob", "JobHandlers.OfferJob", "Api", null, null, null);
        var snapshot = new GraphSnapshot
        {
            SolutionPath = "test",
            BuiltAt = DateTimeOffset.UtcNow,
            Nodes = [endpoint, command, handler],
            Edges =
            [
                new GraphEdge("endpoint", "command", GraphRelation.Dispatches, GraphConfidence.Extracted, null, null),
                new GraphEdge("handler", "command", GraphRelation.Handles, GraphConfidence.Extracted, null, null)
            ]
        };

        await using var database = await GraphDatabase.OpenAsync(databasePath);
        await database.ReplaceSnapshotAsync(snapshot);

        var paths = await new GraphQueryService().FindPathsAsync(database, "JobHandlers.OfferJob", "OfferJobCommandHandler");
        Assert.NotEmpty(paths);

        var relations = paths[0].Steps
            .Where(step => step.IncomingEdge is not null)
            .Select(step => step.IncomingEdge!.Relation)
            .ToList();
        Assert.Equal([GraphRelation.Dispatches, GraphRelation.Handles], relations);
    }
}
