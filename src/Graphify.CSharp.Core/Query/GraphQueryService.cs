using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Storage;

namespace Graphify.CSharp.Core.Query;

public sealed record GraphPathStep(
    GraphNode Node,
    GraphEdge? IncomingEdge);

public sealed record GraphPath(
    IReadOnlyList<GraphPathStep> Steps);

public sealed class GraphQueryService
{
    private const int DefaultMaxVisitedNodes = 25_000;

    public async Task<IReadOnlyList<GraphPath>> FindPathsAsync(
        GraphDatabase database,
        string fromQuery,
        string toQuery,
        int maxDepth = 8,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        var fromNodes = await database.SearchPathEndpointsAsync(fromQuery, limit: 3, cancellationToken).ConfigureAwait(false);
        var toNodes = await database.SearchPathEndpointsAsync(toQuery, limit: 3, cancellationToken).ConfigureAwait(false);

        if (fromNodes.Count == 0 || toNodes.Count == 0)
        {
            return [];
        }

        var pathIndex = await database.LoadPathIndexAsync(cancellationToken).ConfigureAwait(false);
        var targetIds = toNodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var paths = new List<GraphPath>();

        foreach (var start in fromNodes)
        {
            BreadthFirstSearch(pathIndex, start.Id, targetIds, maxDepth, maxResults, paths);
            if (paths.Count >= maxResults)
            {
                break;
            }
        }

        return paths;
    }

    public async Task<SymbolExplanation> ExplainAsync(
        GraphDatabase database,
        string query,
        CancellationToken cancellationToken = default)
    {
        var matches = await database.SearchNodesAsync(query, limit: 1, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (matches.Count == 0)
        {
            return new SymbolExplanation(null, [], []);
        }

        var node = matches[0];
        var incoming = await database.GetIncomingEdgesAsync(node.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        var outgoing = await database.GetOutgoingEdgesAsync(node.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new SymbolExplanation(node, incoming, outgoing);
    }

    public async Task<IReadOnlyList<string>> FindGapsAsync(
        GraphDatabase database,
        string fromQuery,
        string toQuery,
        CancellationToken cancellationToken = default)
    {
        var paths = await FindPathsAsync(database, fromQuery, toQuery, maxDepth: 10, maxResults: 1, cancellationToken).ConfigureAwait(false);
        if (paths.Count > 0)
        {
            return ["A knowledge path already exists between the requested symbols."];
        }

        var fromNodes = await database.SearchPathEndpointsAsync(fromQuery, limit: 1, cancellationToken).ConfigureAwait(false);
        var toNodes = await database.SearchPathEndpointsAsync(toQuery, limit: 1, cancellationToken).ConfigureAwait(false);
        if (fromNodes.Count == 0 || toNodes.Count == 0)
        {
            return ["Could not resolve one or both symbols in the graph."];
        }

        var from = fromNodes[0];
        var to = toNodes[0];
        var suggestions = new List<string>
        {
            $"No path found from '{from.FullName ?? from.Name}' to '{to.FullName ?? to.Name}'.",
            "Possible gaps to investigate:"
        };

        var outgoing = await database.GetOutgoingEdgesAsync(from.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (outgoing.Count == 0)
        {
            suggestions.Add("- Source symbol has no outgoing relationships; it may be isolated or only referenced.");
        }

        var incoming = await database.GetIncomingEdgesAsync(to.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (incoming.Count == 0)
        {
            suggestions.Add("- Target symbol has no incoming relationships; nothing in the graph reaches it yet.");
        }

        var callOutgoing = outgoing
            .Where(edge => edge.Relation is GraphRelation.Calls or GraphRelation.Dispatches)
            .Take(5)
            .ToList();

        if (callOutgoing.Count > 0)
        {
            suggestions.Add("- Nearby calls from the source that might be part of an incomplete flow:");
            foreach (var edge in callOutgoing)
            {
                var target = await database.GetNodeAsync(edge.TargetId, cancellationToken).ConfigureAwait(false);
                suggestions.Add($"  • {edge.Relation} -> {target?.FullName ?? edge.TargetId} ({edge.Confidence})");
            }
        }

        return suggestions;
    }

    private static void BreadthFirstSearch(
        GraphPathIndex pathIndex,
        string startId,
        HashSet<string> targetIds,
        int maxDepth,
        int maxResults,
        List<GraphPath> results,
        int maxVisitedNodes = DefaultMaxVisitedNodes)
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

public sealed record SymbolExplanation(
    GraphNode? Node,
    IReadOnlyList<GraphEdge> Incoming,
    IReadOnlyList<GraphEdge> Outgoing);
