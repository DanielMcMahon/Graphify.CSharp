using Graphify.CSharp.Core.Models;
using Microsoft.Data.Sqlite;

namespace Graphify.CSharp.Core.Storage;

public sealed partial class GraphDatabase
{
  private static readonly NodeKind[] UiNodeKinds =
  [
      NodeKind.UiSurface,
      NodeKind.UiFragment,
      NodeKind.UiElement,
      NodeKind.UiGate,
      NodeKind.UiBinding,
      NodeKind.UiAction,
      NodeKind.UiSelectorHint
  ];

  public async Task<IReadOnlyList<GraphNode>> SearchUiNodesAsync(
      string query,
      int limit = 25,
      CancellationToken cancellationToken = default)
  {
      var results = new List<GraphNode>();
      await using var command = _connection.CreateCommand();
      var kindFilter = string.Join(", ", UiNodeKinds.Select(kind => $"'{kind}'"));
      command.CommandText = $"""
          SELECT id, kind, name, full_name, assembly, file_path, line, end_line, metadata_json
          FROM nodes
          WHERE kind IN ({kindFilter})
            AND (
                lower(name) LIKE $pattern
                OR lower(full_name) LIKE $pattern
                OR lower(id) LIKE $pattern
            )
          ORDER BY
              CASE
                  WHEN lower(name) = lower($exact) THEN 0
                  WHEN lower(full_name) = lower($exact) THEN 1
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

      return results;
  }

  public async Task<IReadOnlyList<GraphNode>> GetNodesByKindAsync(
      NodeKind kind,
      int limit = 100,
      CancellationToken cancellationToken = default)
  {
      var results = new List<GraphNode>();
      await using var command = _connection.CreateCommand();
      command.CommandText = """
          SELECT id, kind, name, full_name, assembly, file_path, line, end_line, metadata_json
          FROM nodes
          WHERE kind = $kind
          ORDER BY name
          LIMIT $limit
          """;
      command.Parameters.AddWithValue("$kind", kind.ToString());
      command.Parameters.AddWithValue("$limit", limit);

      await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
      while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
      {
          results.Add(ReadNode(reader));
      }

      return results;
  }

  public async Task<GraphNode?> FindUiSurfaceAsync(string query, CancellationToken cancellationToken = default)
  {
      var surfaces = await SearchUiNodesAsync(query, limit: 10, cancellationToken).ConfigureAwait(false);
      return surfaces.FirstOrDefault(node => node.Kind == NodeKind.UiSurface)
             ?? surfaces.FirstOrDefault();
  }
}
