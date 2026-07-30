using Graphify.CSharp.Core;
using Graphify.CSharp.Core.Models;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Graphify.CSharp.Core.Storage;

public sealed partial class GraphDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private GraphDatabase(SqliteConnection connection) => _connection = connection;

    public static async Task<GraphDatabase> OpenAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var database = new GraphDatabase(connection);
        await database.InitializeSchemaAsync(cancellationToken).ConfigureAwait(false);
        return database;
    }

    public async Task ReplaceSnapshotAsync(GraphSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteNonQueryAsync(
            transaction,
            "DELETE FROM edges; DELETE FROM nodes; DELETE FROM metadata;",
            cancellationToken).ConfigureAwait(false);

        foreach (var node in snapshot.Nodes)
        {
            await InsertNodeAsync(transaction, node, cancellationToken).ConfigureAwait(false);
        }

        foreach (var edge in snapshot.Edges)
        {
            await InsertEdgeAsync(transaction, edge, cancellationToken).ConfigureAwait(false);
        }

        await UpsertMetadataAsync(transaction, "solution_path", snapshot.SolutionPath, cancellationToken).ConfigureAwait(false);
        await UpsertMetadataAsync(transaction, "built_at", snapshot.BuiltAt.ToString("O"), cancellationToken).ConfigureAwait(false);
        await UpsertMetadataAsync(transaction, "node_count", snapshot.Nodes.Count.ToString(), cancellationToken).ConfigureAwait(false);
        await UpsertMetadataAsync(transaction, "edge_count", snapshot.Edges.Count.ToString(), cancellationToken).ConfigureAwait(false);
        await UpsertMetadataAsync(
            transaction,
            "user_assemblies",
            JsonSerializer.Serialize(snapshot.UserAssemblies),
            cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserCodeContext> GetUserCodeContextAsync(CancellationToken cancellationToken = default)
    {
        var metadata = await GetMetadataAsync(cancellationToken).ConfigureAwait(false);
        var context = UserCodeContext.FromMetadata(metadata);
        if (context.UserAssemblies.Count > 0)
        {
            return context;
        }

        var assemblies = await GetDistinctAssembliesAsync(cancellationToken).ConfigureAwait(false);
        var inferred = assemblies
            .Where(assembly => !UserCodeContext.IsFrameworkAssembly(assembly) && !UserCodeContext.IsTestAssembly(assembly))
            .ToArray();

        return UserCodeContext.FromAssemblies(inferred);
    }

    public async Task<IReadOnlyList<GraphNode>> SearchNodesAsync(
        string query,
        int limit = 25,
        bool justMyCode = false,
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
                    ELSE 2
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

        if (!justMyCode)
        {
            return results;
        }

        var userCode = await GetUserCodeContextAsync(cancellationToken).ConfigureAwait(false);
        return results.Where(node => userCode.IsUserNode(node)).Take(limit).ToList();
    }

    private async Task<IReadOnlyList<string>> GetDistinctAssembliesAsync(CancellationToken cancellationToken)
    {
        var assemblies = new List<string>();
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT assembly FROM nodes WHERE assembly IS NOT NULL ORDER BY assembly";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            assemblies.Add(reader.GetString(0));
        }

        return assemblies;
    }

    public async Task<GraphNode?> GetNodeAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, kind, name, full_name, assembly, file_path, line, end_line, metadata_json
            FROM nodes
            WHERE id = $id
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadNode(reader) : null;
    }

    public async Task<IReadOnlyList<GraphEdge>> GetOutgoingEdgesAsync(string nodeId, string? relation = null, CancellationToken cancellationToken = default)
    {
        return await GetEdgesAsync("source_id", nodeId, relation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GraphEdge>> GetIncomingEdgesAsync(string nodeId, string? relation = null, CancellationToken cancellationToken = default)
    {
        return await GetEdgesAsync("target_id", nodeId, relation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM metadata";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            metadata[reader.GetString(0)] = reader.GetString(1);
        }

        return metadata;
    }

    public async Task<GraphSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var metadata = await GetMetadataAsync(cancellationToken).ConfigureAwait(false);
        var nodes = new List<GraphNode>();
        var edges = new List<GraphEdge>();

        await using (var command = _connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, kind, name, full_name, assembly, file_path, line, end_line, metadata_json
                FROM nodes
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                nodes.Add(ReadNode(reader));
            }
        }

        await using (var command = _connection.CreateCommand())
        {
            command.CommandText = """
                SELECT source_id, target_id, relation, confidence, source_file, line, metadata_json
                FROM edges
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                edges.Add(ReadEdge(reader));
            }
        }

        metadata.TryGetValue("solution_path", out var solutionPath);
        metadata.TryGetValue("built_at", out var builtAtValue);
        var builtAt = DateTimeOffset.TryParse(builtAtValue, out var parsedBuiltAt)
            ? parsedBuiltAt
            : DateTimeOffset.UtcNow;

        IReadOnlyList<string> userAssemblies = [];
        if (metadata.TryGetValue("user_assemblies", out var userAssembliesJson) &&
            !string.IsNullOrWhiteSpace(userAssembliesJson))
        {
            userAssemblies = JsonSerializer.Deserialize<List<string>>(userAssembliesJson) ?? [];
        }

        return new GraphSnapshot
        {
            SolutionPath = solutionPath ?? string.Empty,
            BuiltAt = builtAt,
            Nodes = nodes,
            Edges = edges,
            UserAssemblies = userAssemblies
        };
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private async Task InitializeSchemaAsync(CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            _connection,
            """
            CREATE TABLE IF NOT EXISTS nodes (
                id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                name TEXT NOT NULL,
                full_name TEXT,
                assembly TEXT,
                file_path TEXT,
                line INTEGER,
                end_line INTEGER,
                metadata_json TEXT
            );

            CREATE TABLE IF NOT EXISTS edges (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                source_id TEXT NOT NULL,
                target_id TEXT NOT NULL,
                relation TEXT NOT NULL,
                confidence TEXT NOT NULL,
                source_file TEXT,
                line INTEGER,
                metadata_json TEXT,
                FOREIGN KEY (source_id) REFERENCES nodes(id),
                FOREIGN KEY (target_id) REFERENCES nodes(id)
            );

            CREATE TABLE IF NOT EXISTS metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_nodes_full_name ON nodes(full_name);
            CREATE INDEX IF NOT EXISTS idx_nodes_name ON nodes(name);
            CREATE INDEX IF NOT EXISTS idx_edges_source_relation ON edges(source_id, relation);
            CREATE INDEX IF NOT EXISTS idx_edges_target_relation ON edges(target_id, relation);
            """,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertNodeAsync(SqliteTransaction transaction, GraphNode node, CancellationToken cancellationToken)
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO nodes (id, kind, name, full_name, assembly, file_path, line, end_line, metadata_json)
            VALUES ($id, $kind, $name, $full_name, $assembly, $file_path, $line, $end_line, $metadata_json)
            """;
        command.Parameters.AddWithValue("$id", node.Id);
        command.Parameters.AddWithValue("$kind", node.Kind.ToString());
        command.Parameters.AddWithValue("$name", node.Name);
        command.Parameters.AddWithValue("$full_name", (object?)node.FullName ?? DBNull.Value);
        command.Parameters.AddWithValue("$assembly", (object?)node.Assembly ?? DBNull.Value);
        command.Parameters.AddWithValue("$file_path", (object?)node.FilePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$line", (object?)node.Line ?? DBNull.Value);
        command.Parameters.AddWithValue("$end_line", (object?)node.EndLine ?? DBNull.Value);
        command.Parameters.AddWithValue("$metadata_json", (object?)node.MetadataJson ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertEdgeAsync(SqliteTransaction transaction, GraphEdge edge, CancellationToken cancellationToken)
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO edges (source_id, target_id, relation, confidence, source_file, line, metadata_json)
            VALUES ($source_id, $target_id, $relation, $confidence, $source_file, $line, $metadata_json)
            """;
        command.Parameters.AddWithValue("$source_id", edge.SourceId);
        command.Parameters.AddWithValue("$target_id", edge.TargetId);
        command.Parameters.AddWithValue("$relation", edge.Relation);
        command.Parameters.AddWithValue("$confidence", edge.Confidence.ToString());
        command.Parameters.AddWithValue("$source_file", (object?)edge.SourceFile ?? DBNull.Value);
        command.Parameters.AddWithValue("$line", (object?)edge.Line ?? DBNull.Value);
        command.Parameters.AddWithValue("$metadata_json", (object?)edge.MetadataJson ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertMetadataAsync(SqliteTransaction transaction, string key, string value, CancellationToken cancellationToken)
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO metadata (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<GraphEdge>> GetEdgesAsync(string column, string nodeId, string? relation, CancellationToken cancellationToken)
    {
        var edges = new List<GraphEdge>();
        await using var command = _connection.CreateCommand();
        command.CommandText = relation is null
            ? $"SELECT source_id, target_id, relation, confidence, source_file, line, metadata_json FROM edges WHERE {column} = $node_id"
            : $"SELECT source_id, target_id, relation, confidence, source_file, line, metadata_json FROM edges WHERE {column} = $node_id AND relation = $relation";
        command.Parameters.AddWithValue("$node_id", nodeId);
        if (relation is not null)
        {
            command.Parameters.AddWithValue("$relation", relation);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            edges.Add(ReadEdge(reader));
        }

        return edges;
    }

    private static GraphNode ReadNode(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            Enum.Parse<NodeKind>(reader.GetString(1)),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetInt32(6),
            reader.IsDBNull(7) ? null : reader.GetInt32(7),
            reader.IsDBNull(8) ? null : reader.GetString(8));

    private static GraphEdge ReadEdge(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            Enum.Parse<GraphConfidence>(reader.GetString(3)),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetString(6));

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Task ExecuteNonQueryAsync(SqliteTransaction transaction, string sql, CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(transaction.Connection!, sql, cancellationToken);
}
