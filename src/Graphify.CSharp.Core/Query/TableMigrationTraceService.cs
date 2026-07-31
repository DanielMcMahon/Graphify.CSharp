using System.Text;
using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Storage;

namespace Graphify.CSharp.Core.Query;

public sealed record TableMigrationTraceResult(
    string TableName,
    string Markdown,
    IReadOnlyList<string> Pages,
    IReadOnlyList<string> FileFields,
    IReadOnlyList<string> FileStorageTouchpoints,
    IReadOnlyList<string> MigrationNotes);

public sealed class TableMigrationTraceService
{
    private static readonly string[] UpstreamRelations =
    [
        GraphRelation.MapsToTable,
        GraphRelation.QueriesTable,
        GraphRelation.Calls,
        GraphRelation.References,
        GraphRelation.Injects,
        GraphRelation.Handles,
        GraphRelation.Dispatches,
        GraphRelation.Routes,
        GraphRelation.PageCodeBehind,
        GraphRelation.HasFileField,
        GraphRelation.UsesFileStorage
    ];

    public async Task<TableMigrationTraceResult> TraceAsync(
        GraphDatabase database,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        var tables = await database.SearchTableNodesAsync(tableName, cancellationToken).ConfigureAwait(false);
        if (tables.Count == 0)
        {
            var empty = $"""No table node matched "{tableName}". Rebuild the graph after adding EF/Dapper/SQL extractors, or check the table name.""";
            return new TableMigrationTraceResult(tableName, empty, [], [], [], []);
        }

        var table = tables[0];
        var upstream = await TraceUpstreamAsync(database, table.Id, maxDepth: 8, cancellationToken).ConfigureAwait(false);
        var pages = upstream.Where(node => node.Kind == NodeKind.Page).Select(FormatNode).ToList();
        var fileFields = await CollectFileFieldsAsync(database, table.Id, cancellationToken).ConfigureAwait(false);
        var storageTouchpoints = upstream
            .Where(node => node.Kind == NodeKind.FileField
                           || (node.MetadataJson?.Contains("file_storage", StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(FormatNode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var entities = await GetRelatedNodesAsync(database, table.Id, GraphRelation.MapsToTable, incoming: true, cancellationToken).ConfigureAwait(false);
        var querySites = await GetRelatedNodesAsync(database, table.Id, GraphRelation.QueriesTable, incoming: true, cancellationToken).ConfigureAwait(false);
        var migrationNotes = BuildMigrationNotes(table, entities, fileFields, pages, storageTouchpoints);

        var builder = new StringBuilder();
        builder.AppendLine($"# Migration trace: `{table.Name}`");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine($"Table `{table.Name}` is referenced by {entities.Count} mapped entity type(s), {querySites.Count} direct SQL query site(s), {pages.Count} ASPX page(s), and {fileFields.Count} file-related column(s).");
        builder.AppendLine();

        if (entities.Count > 0)
        {
            builder.AppendLine("## Mapped entities");
            foreach (var entity in entities)
            {
                builder.AppendLine($"- {FormatNode(entity)}");
            }

            builder.AppendLine();
        }

        if (fileFields.Count > 0)
        {
            builder.AppendLine("## File-related columns");
            foreach (var field in fileFields)
            {
                builder.AppendLine($"- `{field}`");
            }

            builder.AppendLine();
        }

        if (querySites.Count > 0)
        {
            builder.AppendLine("## Direct SQL / Dapper touchpoints");
            foreach (var site in querySites)
            {
                builder.AppendLine($"- {FormatNode(site)}");
            }

            builder.AppendLine();
        }

        if (pages.Count > 0)
        {
            builder.AppendLine("## ASPX / WebForms pages");
            foreach (var page in pages)
            {
                builder.AppendLine($"- {page}");
            }

            builder.AppendLine();
        }

        if (storageTouchpoints.Count > 0)
        {
            builder.AppendLine("## File storage touchpoints");
            foreach (var touchpoint in storageTouchpoints)
            {
                builder.AppendLine($"- {touchpoint}");
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Upstream call chain");
        foreach (var node in upstream.Where(node => node.Kind is NodeKind.Method or NodeKind.Type or NodeKind.Page).Take(40))
        {
            builder.AppendLine($"- {FormatNode(node)}");
        }

        builder.AppendLine();
        builder.AppendLine("## Migration notes");
        foreach (var note in migrationNotes)
        {
            builder.AppendLine($"- {note}");
        }

        return new TableMigrationTraceResult(
            table.Name,
            builder.ToString(),
            pages,
            fileFields,
            storageTouchpoints,
            migrationNotes);
    }

    private static async Task<List<GraphNode>> TraceUpstreamAsync(
        GraphDatabase database,
        string startId,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        var results = new List<GraphNode>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { startId };
        var queue = new Queue<(string NodeId, int Depth)>();
        queue.Enqueue((startId, 0));

        while (queue.Count > 0)
        {
            var (nodeId, depth) = queue.Dequeue();
            if (depth >= maxDepth)
            {
                continue;
            }

            foreach (var relation in UpstreamRelations)
            {
                var incoming = await database.GetIncomingEdgesAsync(nodeId, relation, cancellationToken).ConfigureAwait(false);
                foreach (var edge in incoming)
                {
                    if (!visited.Add(edge.SourceId))
                    {
                        continue;
                    }

                    var node = await database.GetNodeAsync(edge.SourceId, cancellationToken).ConfigureAwait(false);
                    if (node is null)
                    {
                        continue;
                    }

                    results.Add(node);
                    queue.Enqueue((edge.SourceId, depth + 1));
                }
            }
        }

        return results;
    }

    private static async Task<IReadOnlyList<string>> CollectFileFieldsAsync(GraphDatabase database, string tableId, CancellationToken cancellationToken)
    {
        var entities = await GetRelatedNodesAsync(database, tableId, GraphRelation.MapsToTable, incoming: true, cancellationToken).ConfigureAwait(false);
        var fields = new List<string>();
        foreach (var entity in entities)
        {
            var related = await GetRelatedNodesAsync(database, entity.Id, GraphRelation.HasFileField, incoming: false, cancellationToken).ConfigureAwait(false);
            fields.AddRange(related.Select(node => node.FullName ?? node.Name));
        }

        return fields.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<IReadOnlyList<GraphNode>> GetRelatedNodesAsync(
        GraphDatabase database,
        string nodeId,
        string relation,
        bool incoming,
        CancellationToken cancellationToken)
    {
        var edges = incoming
            ? await database.GetIncomingEdgesAsync(nodeId, relation, cancellationToken).ConfigureAwait(false)
            : await database.GetOutgoingEdgesAsync(nodeId, relation, cancellationToken).ConfigureAwait(false);

        var nodes = new List<GraphNode>();
        foreach (var edge in edges)
        {
            var otherId = incoming ? edge.SourceId : edge.TargetId;
            var node = await database.GetNodeAsync(otherId, cancellationToken).ConfigureAwait(false);
            if (node is not null)
            {
                nodes.Add(node);
            }
        }

        return nodes;
    }

    private static IReadOnlyList<string> BuildMigrationNotes(
        GraphNode table,
        IReadOnlyList<GraphNode> entities,
        IReadOnlyList<string> fileFields,
        IReadOnlyList<string> pages,
        IReadOnlyList<string> storageTouchpoints)
    {
        var notes = new List<string>
        {
            $"Start migration at table `{table.Name}` and verify entity mappings before changing repositories.",
            "Replace local file path reads/writes with blob storage adapters behind existing service interfaces where possible."
        };

        if (fileFields.Count > 0)
        {
            notes.Add($"Migrate file path columns first: {string.Join(", ", fileFields)}.");
        }

        if (pages.Count > 0)
        {
            notes.Add("Update ASPX/code-behind pages that surface file upload/download for this table.");
        }

        if (storageTouchpoints.Count > 0)
        {
            notes.Add("Audit direct System.IO / blob API usage in the upstream chain and route through a single storage abstraction.");
        }

        if (entities.Count > 0)
        {
            notes.Add($"Confirm EF/Dapper queries for {string.Join(", ", entities.Select(entity => entity.Name))} after storage migration.");
        }

        return notes;
    }

    private static string FormatNode(GraphNode node)
    {
        var location = string.IsNullOrWhiteSpace(node.FilePath) ? string.Empty : $" ({node.FilePath}:{node.Line})";
        return $"{node.Kind}: {node.FullName ?? node.Name}{location}";
    }
}
