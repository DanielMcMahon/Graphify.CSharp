using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Storage;

namespace Graphify.CSharp.Core.Storage;

public sealed partial class GraphDatabase
{
    public static readonly string[] CompositionRelations =
    [
        GraphRelation.Calls,
        GraphRelation.Registers,
        GraphRelation.Injects,
        GraphRelation.Contains,
        GraphRelation.Implements
    ];

    public async Task<IReadOnlyList<GraphNode>> FindStartupEntryPointsAsync(
        bool justMyCode = true,
        string? fileFilter = null,
        CancellationToken cancellationToken = default)
    {
        var nodes = new List<GraphNode>();
        await using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, kind, name, full_name, assembly, file_path, line, end_line, metadata_json
            FROM nodes
            WHERE file_path LIKE '%Program.cs'
              AND file_path NOT LIKE '%.nuget%'
              AND file_path NOT LIKE '%IntegrationTests%'
              AND kind IN ('Method', 'Type')
            ORDER BY
                CASE WHEN kind = 'Method' THEN 0 ELSE 1 END,
                file_path,
                line
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            nodes.Add(ReadNode(reader));
        }

        if (!string.IsNullOrWhiteSpace(fileFilter))
        {
            nodes = nodes
                .Where(node => node.FilePath?.Contains(fileFilter, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }

        if (!justMyCode)
        {
            return PreferStartupEntryPoints(nodes);
        }

        var userCode = await GetUserCodeContextAsync(cancellationToken).ConfigureAwait(false);
        return PreferStartupEntryPoints(nodes.Where(node => userCode.IsUserNode(node)).ToList());
    }

    public async Task<GraphExport> GetCompositionSubgraphAsync(
        IReadOnlyList<string> centerNodeIds,
        int depth = 4,
        int maxNodes = 250,
        IReadOnlyList<string>? relations = null,
        bool justMyCode = true,
        CancellationToken cancellationToken = default)
    {
        var metadata = await GetMetadataAsync(cancellationToken).ConfigureAwait(false);
        var userCode = justMyCode
            ? await GetUserCodeContextAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var relationFilter = (relations?.Count > 0 ? relations : CompositionRelations)
            .ToHashSet(StringComparer.Ordinal);

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var centerNodeId in centerNodeIds)
        {
            if (!string.IsNullOrWhiteSpace(centerNodeId))
            {
                nodeIds.Add(centerNodeId);
            }
        }

        if (nodeIds.Count == 0)
        {
            return new GraphExport([], [], metadata);
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

    private static IReadOnlyList<GraphNode> PreferStartupEntryPoints(IReadOnlyList<GraphNode> nodes)
    {
        var mains = nodes
            .Where(node => node.Kind == NodeKind.Method &&
                           (node.Name is "Main" or "<Main>$" || node.Name.Contains("Main", StringComparison.Ordinal)))
            .ToList();
        if (mains.Count > 0)
        {
            return mains;
        }

        var programs = nodes
            .Where(node => node.Kind == NodeKind.Type && node.Name == "Program")
            .ToList();
        return programs.Count > 0 ? programs : nodes;
    }
}
