using System.Text;
using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Storage;
using Graphify.CSharp.Core.Workspace;

namespace Graphify.CSharp.Core.Query;

public sealed class GraphReportGenerator
{
    public async Task<string> GenerateAsync(GraphDatabase database, CancellationToken cancellationToken = default)
    {
        var metadata = await database.GetMetadataAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await database.LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var hubNodes = FindHubNodes(snapshot);

        var builder = new StringBuilder();
        builder.AppendLine("# Graphify.CSharp Architecture Report");
        builder.AppendLine();
        builder.AppendLine($"Built: {metadata.GetValueOrDefault("built_at", "unknown")}");
        builder.AppendLine($"Solution: {metadata.GetValueOrDefault("solution_path", "unknown")}");
        builder.AppendLine($"Nodes: {snapshot.Nodes.Count:N0} · Edges: {snapshot.Edges.Count:N0}");
        builder.AppendLine();
        builder.AppendLine("## Ask your agent");
        builder.AppendLine();
        builder.AppendLine("- How does `OfferJob` work?");
        builder.AppendLine("- Trace the flow from `JobsController` to the database");
        builder.AppendLine("- Who calls `INotifier`?");
        builder.AppendLine("- Explain `OrderService`");
        builder.AppendLine();
        builder.AppendLine("## Hub symbols");
        builder.AppendLine();
        foreach (var hub in hubNodes)
        {
            builder.AppendLine($"- `{hub.FullName ?? hub.Name}` ({hub.Degree} connections, {hub.Kind})");
        }

        builder.AppendLine();
        builder.AppendLine("## Suggested investigations");
        builder.AppendLine();
        foreach (var hub in hubNodes.Take(5))
        {
            builder.AppendLine($"- How does `{hub.Name}` work?");
        }

        return builder.ToString();
    }

    public async Task WriteAsync(string projectRoot, GraphDatabase database, CancellationToken cancellationToken = default)
    {
        var report = await GenerateAsync(database, cancellationToken).ConfigureAwait(false);
        var reportPath = GraphWorkspace.GetReportPath(projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllTextAsync(reportPath, report, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<HubNode> FindHubNodes(GraphSnapshot snapshot)
    {
        var degrees = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var edge in snapshot.Edges)
        {
            degrees[edge.SourceId] = degrees.GetValueOrDefault(edge.SourceId) + 1;
            degrees[edge.TargetId] = degrees.GetValueOrDefault(edge.TargetId) + 1;
        }

        var nodesById = snapshot.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        return degrees
            .Where(pair => nodesById.ContainsKey(pair.Key))
            .OrderByDescending(pair => pair.Value)
            .Take(15)
            .Select(pair =>
            {
                var node = nodesById[pair.Key];
                return new HubNode(node, pair.Value);
            })
            .ToList();
    }

    private sealed record HubNode(GraphNode Node, int Degree)
    {
        public string Name => Node.Name;
        public string? FullName => Node.FullName;
        public string Kind => Node.Kind.ToString();
    }
}
