using Graphify.CSharp.Core;
using Graphify.CSharp.Core.Models;
using Microsoft.Data.Sqlite;

namespace Graphify.CSharp.Core.Storage;

public sealed partial class GraphDatabase
{
    public async Task<GraphOverview> GetOverviewAsync(
        int seedLimit = 40,
        bool justMyCode = false,
        CancellationToken cancellationToken = default)
    {
        var metadata = await GetMetadataAsync(cancellationToken).ConfigureAwait(false);
        var nodeCounts = await GetCountMapAsync("SELECT kind, COUNT(*) FROM nodes GROUP BY kind", cancellationToken).ConfigureAwait(false);
        var edgeCounts = await GetCountMapAsync("SELECT relation, COUNT(*) FROM edges GROUP BY relation", cancellationToken).ConfigureAwait(false);
        var seedNodes = await GetSeedNodesAsync(seedLimit, justMyCode, cancellationToken).ConfigureAwait(false);

        return new GraphOverview(metadata, nodeCounts, edgeCounts, seedNodes);
    }

    public async Task<NodeDetail> GetNodeDetailAsync(
        string nodeId,
        bool justMyCode = false,
        CancellationToken cancellationToken = default)
    {
        var node = await GetNodeAsync(nodeId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Node not found: {nodeId}");

        var userCode = justMyCode
            ? await GetUserCodeContextAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var incoming = await GetIncomingEdgesAsync(nodeId, cancellationToken: cancellationToken).ConfigureAwait(false);
        var outgoing = await GetOutgoingEdgesAsync(nodeId, cancellationToken: cancellationToken).ConfigureAwait(false);

        var callers = await ToLinkedEdgesAsync(incoming, isIncoming: true, GraphRelation.Calls, userCode, cancellationToken).ConfigureAwait(false);
        var callees = await ToLinkedEdgesAsync(outgoing, isIncoming: false, GraphRelation.Calls, userCode, cancellationToken).ConfigureAwait(false);
        var referencesIn = await ToLinkedEdgesAsync(incoming, isIncoming: true, GraphRelation.References, userCode, cancellationToken).ConfigureAwait(false);
        var referencesOut = await ToLinkedEdgesAsync(outgoing, isIncoming: false, GraphRelation.References, userCode, cancellationToken).ConfigureAwait(false);

        var otherIncoming = await ToLinkedEdgesAsync(
            incoming.Where(edge => edge.Relation is not GraphRelation.Calls and not GraphRelation.References).ToList(),
            isIncoming: true,
            relationFilter: null,
            userCode,
            cancellationToken).ConfigureAwait(false);

        var otherOutgoing = await ToLinkedEdgesAsync(
            outgoing.Where(edge => edge.Relation is not GraphRelation.Calls and not GraphRelation.References).ToList(),
            isIncoming: false,
            relationFilter: null,
            userCode,
            cancellationToken).ConfigureAwait(false);

        return new NodeDetail(node, callers, callees, referencesIn, referencesOut, otherIncoming, otherOutgoing);
    }

    public async Task<GraphExport> GetSubgraphAsync(
        string? centerNodeId = null,
        int depth = 2,
        int maxNodes = 300,
        IReadOnlyList<string>? relations = null,
        bool justMyCode = false,
        CancellationToken cancellationToken = default)
    {
        var metadata = await GetMetadataAsync(cancellationToken).ConfigureAwait(false);
        var userCode = justMyCode
            ? await GetUserCodeContextAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var relationFilter = relations?.Count > 0
            ? relations.ToHashSet(StringComparer.Ordinal)
            : null;

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(centerNodeId))
        {
            foreach (var seed in await GetSeedNodesAsync(Math.Min(maxNodes, 40), justMyCode, cancellationToken).ConfigureAwait(false))
            {
                nodeIds.Add(seed.Id);
            }
        }
        else
        {
            nodeIds.Add(centerNodeId);
        }

        var frontier = new Queue<string>(nodeIds);
        var visitedDepth = nodeIds.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);

        while (frontier.Count > 0 && nodeIds.Count < maxNodes)
        {
            var currentId = frontier.Dequeue();
            var currentDepth = visitedDepth[currentId];
            if (currentDepth >= depth)
            {
                continue;
            }

            var neighbors = await GetNeighborIdsAsync(currentId, relationFilter, cancellationToken).ConfigureAwait(false);
            foreach (var (neighborId, _) in neighbors)
            {
                if (!nodeIds.Add(neighborId))
                {
                    continue;
                }

                if (userCode is not null)
                {
                    var neighbor = await GetNodeAsync(neighborId, cancellationToken).ConfigureAwait(false);
                    if (neighbor is null || !userCode.IsUserNode(neighbor))
                    {
                        nodeIds.Remove(neighborId);
                        continue;
                    }
                }

                visitedDepth[neighborId] = currentDepth + 1;
                frontier.Enqueue(neighborId);
                if (nodeIds.Count >= maxNodes)
                {
                    break;
                }
            }
        }

        var nodes = new List<GraphNode>();
        foreach (var id in nodeIds)
        {
            var node = await GetNodeAsync(id, cancellationToken).ConfigureAwait(false);
            if (node is null)
            {
                continue;
            }

            if (userCode is not null && !userCode.IsUserNode(node))
            {
                continue;
            }

            nodes.Add(node);
        }

        var nodeIdSet = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var edges = await GetEdgesBetweenAsync(nodeIdSet, relationFilter, cancellationToken).ConfigureAwait(false);
        return new GraphExport(nodes, edges, metadata);
    }

    private async Task<IReadOnlyList<GraphNode>> GetSeedNodesAsync(
        int limit,
        bool justMyCode,
        CancellationToken cancellationToken)
    {
        var nodes = new List<GraphNode>();
        await using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT n.id, n.kind, n.name, n.full_name, n.assembly, n.file_path, n.line, n.end_line, n.metadata_json
            FROM nodes n
            LEFT JOIN (
                SELECT source_id AS node_id FROM edges
                UNION ALL
                SELECT target_id AS node_id FROM edges
            ) e ON e.node_id = n.id
            WHERE n.kind IN ('Type', 'Method')
            GROUP BY n.id
            ORDER BY COUNT(e.node_id) DESC, n.full_name
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", justMyCode ? limit * 4 : limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            nodes.Add(ReadNode(reader));
        }

        if (!justMyCode)
        {
            return nodes.Take(limit).ToList();
        }

        var userCode = await GetUserCodeContextAsync(cancellationToken).ConfigureAwait(false);
        return nodes.Where(node => userCode.IsUserNode(node)).Take(limit).ToList();
    }

    private async Task<IReadOnlyList<LinkedEdge>> ToLinkedEdgesAsync(
        IReadOnlyList<GraphEdge> edges,
        bool isIncoming,
        string? relationFilter,
        UserCodeContext? userCode,
        CancellationToken cancellationToken)
    {
        var filtered = relationFilter is null
            ? edges
            : edges.Where(edge => edge.Relation == relationFilter).ToList();

        var linked = new List<LinkedEdge>();
        foreach (var edge in filtered)
        {
            var otherId = isIncoming ? edge.SourceId : edge.TargetId;
            var other = await GetNodeAsync(otherId, cancellationToken).ConfigureAwait(false);
            if (other is null)
            {
                continue;
            }

            if (userCode is not null && !userCode.IsUserNode(other))
            {
                continue;
            }

            linked.Add(new LinkedEdge(edge, other, isIncoming));
        }

        return linked;
    }

    private async Task<IReadOnlyDictionary<string, int>> GetCountMapAsync(string sql, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return counts;
    }

    private async Task<IReadOnlyList<(string NeighborId, GraphEdge Edge)>> GetNeighborIdsAsync(
        string nodeId,
        HashSet<string>? relationFilter,
        CancellationToken cancellationToken)
    {
        var neighbors = new List<(string, GraphEdge)>();
        foreach (var edge in await GetOutgoingEdgesAsync(nodeId, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (relationFilter is null || relationFilter.Contains(edge.Relation))
            {
                neighbors.Add((edge.TargetId, edge));
            }
        }

        foreach (var edge in await GetIncomingEdgesAsync(nodeId, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (relationFilter is null || relationFilter.Contains(edge.Relation))
            {
                neighbors.Add((edge.SourceId, edge));
            }
        }

        return neighbors;
    }

    private async Task<IReadOnlyList<GraphEdge>> GetEdgesBetweenAsync(
        HashSet<string> nodeIds,
        HashSet<string>? relationFilter,
        CancellationToken cancellationToken)
    {
        var edges = new Dictionary<string, GraphEdge>(StringComparer.Ordinal);

        foreach (var nodeId in nodeIds)
        {
            foreach (var edge in await GetOutgoingEdgesAsync(nodeId, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                if (!nodeIds.Contains(edge.TargetId))
                {
                    continue;
                }

                if (relationFilter is not null && !relationFilter.Contains(edge.Relation))
                {
                    continue;
                }

                var key = $"{edge.SourceId}|{edge.TargetId}|{edge.Relation}";
                edges[key] = edge;
            }
        }

        return edges.Values.ToList();
    }
}
