using Graphify.CSharp.Core.Models;
using Microsoft.Data.Sqlite;

namespace Graphify.CSharp.Core.Storage;

public sealed record MediatorFootprint(
    int DispatchSites,
    int PublishSites,
    int HandlerCount,
    int CommandCount,
    int NotificationCount,
    int FilesAffected,
    IReadOnlyList<string> DispatchLines,
    IReadOnlyList<string> HandlerLines);

public sealed partial class GraphDatabase
{
    public async Task<MediatorFootprint> GetMediatorFootprintAsync(
        bool justMyCode = true,
        CancellationToken cancellationToken = default)
    {
        var userCode = justMyCode
            ? await GetUserCodeContextAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var dispatchEdges = await LoadMediatorEdgesAsync(GraphRelation.Dispatches, cancellationToken).ConfigureAwait(false);
        var publishEdges = await LoadMediatorEdgesAsync(GraphRelation.Publishes, cancellationToken).ConfigureAwait(false);
        var handleEdges = await LoadMediatorEdgesAsync(GraphRelation.Handles, cancellationToken).ConfigureAwait(false);

        if (userCode is not null)
        {
            dispatchEdges = await FilterEdgesToUserCodeAsync(dispatchEdges, userCode, cancellationToken).ConfigureAwait(false);
            publishEdges = await FilterEdgesToUserCodeAsync(publishEdges, userCode, cancellationToken).ConfigureAwait(false);
            handleEdges = await FilterEdgesToUserCodeAsync(handleEdges, userCode, cancellationToken).ConfigureAwait(false);
        }

        var commandIds = new HashSet<string>(StringComparer.Ordinal);
        var notificationIds = new HashSet<string>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var dispatchLines = new List<string>();
        foreach (var edge in dispatchEdges)
        {
            TrackFile(edge.SourceFile, files);
            var caller = await GetNodeAsync(edge.SourceId, cancellationToken).ConfigureAwait(false);
            var command = await GetNodeAsync(edge.TargetId, cancellationToken).ConfigureAwait(false);
            if (command is not null)
            {
                commandIds.Add(command.Id);
            }

            dispatchLines.Add($"{caller?.FullName ?? edge.SourceId} → {command?.FullName ?? edge.TargetId}");
        }

        var publishLines = new List<string>();
        foreach (var edge in publishEdges)
        {
            TrackFile(edge.SourceFile, files);
            var caller = await GetNodeAsync(edge.SourceId, cancellationToken).ConfigureAwait(false);
            var notification = await GetNodeAsync(edge.TargetId, cancellationToken).ConfigureAwait(false);
            if (notification is not null)
            {
                notificationIds.Add(notification.Id);
            }

            publishLines.Add($"{caller?.FullName ?? edge.SourceId} → {notification?.FullName ?? edge.TargetId}");
        }

        var handlerLines = new List<string>();
        var handlerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in handleEdges)
        {
            TrackFile(edge.SourceFile, files);
            handlerIds.Add(edge.SourceId);
            var handler = await GetNodeAsync(edge.SourceId, cancellationToken).ConfigureAwait(false);
            var message = await GetNodeAsync(edge.TargetId, cancellationToken).ConfigureAwait(false);
            if (message?.Name.EndsWith("Command", StringComparison.Ordinal) == true
                || message?.Name.EndsWith("Query", StringComparison.Ordinal) == true)
            {
                commandIds.Add(message.Id);
            }
            else if (message is not null)
            {
                notificationIds.Add(message.Id);
            }

            handlerLines.Add($"{handler?.FullName ?? edge.SourceId} handles {message?.FullName ?? edge.TargetId}");
        }

        return new MediatorFootprint(
            dispatchEdges.Count,
            publishEdges.Count,
            handlerIds.Count,
            commandIds.Count,
            notificationIds.Count,
            files.Count,
            dispatchLines,
            handlerLines);
    }

    private static void TrackFile(string? sourceFile, HashSet<string> files)
    {
        if (!string.IsNullOrWhiteSpace(sourceFile))
        {
            files.Add(sourceFile);
        }
    }

    private async Task<IReadOnlyList<GraphEdge>> LoadMediatorEdgesAsync(
        string relation,
        CancellationToken cancellationToken)
    {
        var edges = new List<GraphEdge>();
        await using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT source_id, target_id, relation, confidence, source_file, line, metadata_json
            FROM edges
            WHERE relation = $relation
            """;
        command.Parameters.AddWithValue("$relation", relation);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            edges.Add(ReadEdge(reader));
        }

        return edges;
    }

    private async Task<IReadOnlyList<GraphEdge>> FilterEdgesToUserCodeAsync(
        IReadOnlyList<GraphEdge> edges,
        UserCodeContext userCode,
        CancellationToken cancellationToken)
    {
        var filtered = new List<GraphEdge>();
        foreach (var edge in edges)
        {
            var source = await GetNodeAsync(edge.SourceId, cancellationToken).ConfigureAwait(false);
            var target = await GetNodeAsync(edge.TargetId, cancellationToken).ConfigureAwait(false);
            if (source is not null && target is not null
                && userCode.IsUserNode(source)
                && userCode.IsUserNode(target))
            {
                filtered.Add(edge);
            }
        }

        return filtered;
    }
}
