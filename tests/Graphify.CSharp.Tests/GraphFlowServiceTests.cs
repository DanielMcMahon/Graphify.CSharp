using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Query;
using Graphify.CSharp.Core.Storage;

namespace Graphify.CSharp.Tests;

public sealed class GraphFlowServiceTests
{
    [Fact]
    public async Task FindEndpointFlows_follows_route_handler_repository_chain()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"graphify-flow-{Guid.NewGuid():N}.db");
        var endpoint = new GraphNode("endpoint:Api|GET:/orders", NodeKind.Type, "GET /orders", "GET /orders", "Api", null, null, null);
        var handler = new GraphNode("handler", NodeKind.Method, "GetOrders", "OrdersHandler.GetOrders", "Api", null, null, null);
        var repository = new GraphNode("repo", NodeKind.Type, "IOrderRepository", "IOrderRepository", "Sample", null, null, null);
        var snapshot = new GraphSnapshot
        {
            SolutionPath = "test",
            BuiltAt = DateTimeOffset.UtcNow,
            Nodes = [endpoint, handler, repository],
            Edges =
            [
                new GraphEdge(endpoint.Id, handler.Id, GraphRelation.Routes, GraphConfidence.Extracted, null, null),
                new GraphEdge(handler.Id, repository.Id, GraphRelation.Calls, GraphConfidence.Extracted, null, null)
            ]
        };

        await using var database = await GraphDatabase.OpenAsync(databasePath);
        await database.ReplaceSnapshotAsync(snapshot);

        var flows = await new GraphFlowService().FindEndpointFlowsAsync(database, "GET /orders");
        Assert.NotEmpty(flows);
        Assert.Contains(flows[0].Steps, step => step.Node.Id == repository.Id);
    }
}
