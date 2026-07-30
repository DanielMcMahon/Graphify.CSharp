using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Storage;

namespace Graphify.CSharp.Core.Query;

public sealed class GraphFlowService
{
    private static readonly string[] FlowRelations =
    [
        GraphRelation.Routes,
        GraphRelation.Dispatches,
        GraphRelation.Handles,
        GraphRelation.Calls,
        GraphRelation.Injects
    ];

    public async Task<IReadOnlyList<GraphPath>> FindEndpointFlowsAsync(
        GraphDatabase database,
        string endpointQuery,
        int maxDepth = 10,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        var endpoints = await database.SearchPathEndpointsAsync(endpointQuery, limit: 5, cancellationToken).ConfigureAwait(false);
        if (endpoints.Count == 0)
        {
            return [];
        }

        var pathIndex = await database.LoadFlowIndexAsync(cancellationToken).ConfigureAwait(false);
        var repositoryTargets = await database.SearchNodesAsync("Repository", limit: 50, cancellationToken: cancellationToken).ConfigureAwait(false);
        var targetIds = repositoryTargets
            .Where(node => node.Name.Contains("Repository", StringComparison.Ordinal) || (node.FullName?.Contains("Repository", StringComparison.Ordinal) ?? false))
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        if (targetIds.Count == 0)
        {
            return [];
        }

        var results = new List<GraphPath>();
        foreach (var endpoint in endpoints)
        {
            BreadthFirstSearch(pathIndex, endpoint.Id, targetIds, maxDepth, maxResults, results);
            if (results.Count >= maxResults)
            {
                break;
            }
        }

        return results;
    }

    private static void BreadthFirstSearch(
        GraphPathIndex pathIndex,
        string startId,
        HashSet<string> targetIds,
        int maxDepth,
        int maxResults,
        List<GraphPath> results,
        int maxVisitedNodes = 25_000)
    {
        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { startId };
        var parentNode = new Dictionary<string, string>(StringComparer.Ordinal);
        var parentEdge = new Dictionary<string, GraphEdge>(StringComparer.Ordinal);
        var depth = new Dictionary<string, int>(StringComparer.Ordinal) { [startId] = 0 };

        queue.Enqueue(startId);

        while (queue.Count > 0 && results.Count < maxResults && visited.Count < maxVisitedNodes)
        {
            var currentId = queue.Dequeue();
            var currentDepth = depth[currentId];

            if (targetIds.Contains(currentId) && currentId != startId)
            {
                results.Add(new GraphPath(ReconstructPath(pathIndex, startId, currentId, parentNode, parentEdge)));
                continue;
            }

            if (currentDepth >= maxDepth)
            {
                continue;
            }

            foreach (var (neighborId, edge) in pathIndex.GetNeighbors(currentId))
            {
                if (!visited.Add(neighborId))
                {
                    continue;
                }

                parentNode[neighborId] = currentId;
                parentEdge[neighborId] = edge;
                depth[neighborId] = currentDepth + 1;
                queue.Enqueue(neighborId);
            }
        }
    }

    private static IReadOnlyList<GraphPathStep> ReconstructPath(
        GraphPathIndex pathIndex,
        string startId,
        string endId,
        IReadOnlyDictionary<string, string> parentNode,
        IReadOnlyDictionary<string, GraphEdge> parentEdge)
    {
        var nodeIds = new Stack<string>();
        var edges = new Stack<GraphEdge>();
        var current = endId;

        while (current != startId)
        {
            nodeIds.Push(current);
            if (!parentEdge.TryGetValue(current, out var edge) ||
                !parentNode.TryGetValue(current, out var previous))
            {
                break;
            }

            edges.Push(edge);
            current = previous;
        }

        nodeIds.Push(startId);

        var steps = new List<GraphPathStep>();
        var firstNode = pathIndex.GetNode(nodeIds.Pop());
        if (firstNode is null)
        {
            return steps;
        }

        steps.Add(new GraphPathStep(firstNode, null));

        while (nodeIds.Count > 0)
        {
            var edge = edges.Pop();
            var node = pathIndex.GetNode(nodeIds.Pop());
            if (node is not null)
            {
                steps.Add(new GraphPathStep(node, edge));
            }
        }

        return steps;
    }
}
