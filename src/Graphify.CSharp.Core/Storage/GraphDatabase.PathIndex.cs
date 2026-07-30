using Graphify.CSharp.Core.Models;
using Microsoft.Data.Sqlite;

namespace Graphify.CSharp.Core.Storage;

public sealed partial class GraphDatabase
{
    public static readonly string[] PathTraversalRelations =
    [
        GraphRelation.Calls,
        GraphRelation.Inherits,
        GraphRelation.Implements,
        GraphRelation.Overrides,
        GraphRelation.Injects,
        GraphRelation.Dispatches,
        GraphRelation.Publishes
    ];

    public static readonly string[] FlowTraversalRelations =
    [
        GraphRelation.Routes,
        GraphRelation.Dispatches,
        GraphRelation.Handles,
        GraphRelation.Calls,
        GraphRelation.Injects
    ];

    public async Task<GraphPathIndex> LoadFlowIndexAsync(CancellationToken cancellationToken = default) =>
        await LoadRelationIndexAsync(FlowTraversalRelations, reverseHandles: true, cancellationToken).ConfigureAwait(false);

    public async Task<GraphPathIndex> LoadPathIndexAsync(CancellationToken cancellationToken = default) =>
        await LoadRelationIndexAsync(PathTraversalRelations, reverseHandles: true, cancellationToken).ConfigureAwait(false);

    private async Task<GraphPathIndex> LoadRelationIndexAsync(
        IReadOnlyList<string> relations,
        bool reverseHandles,
        CancellationToken cancellationToken)
    {
        var relationFilter = string.Join(", ", relations.Select(_ => $"'{_}'"));
        var adjacency = new Dictionary<string, List<(string NeighborId, GraphEdge Edge)>>(StringComparer.Ordinal);
        var touchedNodeIds = new HashSet<string>(StringComparer.Ordinal);

        await using (var command = _connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT source_id, target_id, relation, confidence, source_file, line, metadata_json
                FROM edges
                WHERE relation IN ({relationFilter})
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var edge = ReadEdge(reader);
                AddNeighbor(adjacency, edge.SourceId, edge.TargetId, edge);
                touchedNodeIds.Add(edge.SourceId);
                touchedNodeIds.Add(edge.TargetId);
            }
        }

        if (reverseHandles)
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT source_id, target_id, relation, confidence, source_file, line, metadata_json
                FROM edges
                WHERE relation = $relation
                """;
            command.Parameters.AddWithValue("$relation", GraphRelation.Handles);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var edge = ReadEdge(reader);
                AddNeighbor(adjacency, edge.TargetId, edge.SourceId, edge);
                touchedNodeIds.Add(edge.SourceId);
                touchedNodeIds.Add(edge.TargetId);
            }
        }

        var nodes = await LoadNodesAsync(touchedNodeIds, cancellationToken).ConfigureAwait(false);
        return new GraphPathIndex(adjacency, nodes);
    }

    public async Task<IReadOnlyList<GraphNode>> SearchPathEndpointsAsync(
        string query,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var results = new List<GraphNode>();
        await using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, kind, name, full_name, assembly, file_path, line, end_line, metadata_json
            FROM nodes
            WHERE lower(name) LIKE $pattern
               OR lower(full_name) LIKE $pattern
               OR lower(id) LIKE $pattern
            ORDER BY
                CASE
                    WHEN lower(full_name) = lower($exact) THEN 0
                    WHEN lower(name) = lower($exact) THEN 1
                    WHEN kind IN ('Method', 'Type') THEN 2
                    ELSE 3
                END,
                length(full_name)
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$pattern", $"%{query.ToLowerInvariant()}%");
        command.Parameters.AddWithValue("$exact", query.ToLowerInvariant());
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadNode(reader));
        }

        return results;
    }

    private async Task<Dictionary<string, GraphNode>> LoadNodesAsync(
        IEnumerable<string> nodeIds,
        CancellationToken cancellationToken)
    {
        var nodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        foreach (var batch in nodeIds.Chunk(500))
        {
            await using var command = _connection.CreateCommand();
            var parameters = batch.Select((id, index) => $"$id{index}").ToArray();
            command.CommandText = $"""
                SELECT id, kind, name, full_name, assembly, file_path, line, end_line, metadata_json
                FROM nodes
                WHERE id IN ({string.Join(", ", parameters)})
                """;

            var index = 0;
            foreach (var id in batch)
            {
                command.Parameters.AddWithValue($"$id{index++}", id);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var node = ReadNode(reader);
                nodes[node.Id] = node;
            }
        }

        return nodes;
    }

    private static void AddNeighbor(
        Dictionary<string, List<(string NeighborId, GraphEdge Edge)>> adjacency,
        string fromId,
        string toId,
        GraphEdge edge)
    {
        if (!adjacency.TryGetValue(fromId, out var neighbors))
        {
            neighbors = [];
            adjacency[fromId] = neighbors;
        }

        neighbors.Add((toId, edge));
    }
}
