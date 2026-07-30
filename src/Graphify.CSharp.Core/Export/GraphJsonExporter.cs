using System.Text.Json;
using System.Text.Json.Serialization;
using Graphify.CSharp.Core.Models;

namespace Graphify.CSharp.Core.Export;

public static class GraphJsonExporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string ToJson(GraphSnapshot snapshot) =>
        JsonSerializer.Serialize(ToDocument(snapshot), SerializerOptions);

    public static async Task WriteAsync(GraphSnapshot snapshot, string outputPath, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(outputPath, ToJson(snapshot), cancellationToken).ConfigureAwait(false);
    }

    public static GraphJsonDocument ToDocument(GraphSnapshot snapshot)
    {
        var nodes = snapshot.Nodes
            .Select(node => new GraphJsonNode
            {
                Id = node.Id,
                Label = node.FullName ?? node.Name,
                FileType = "code",
                SourceFile = node.FilePath,
                Type = node.Kind.ToString(),
                Kind = node.Kind.ToString(),
                FullName = node.FullName,
                Assembly = node.Assembly,
                FilePath = node.FilePath,
                Line = node.Line,
                EndLine = node.EndLine
            })
            .ToList();

        var edges = snapshot.Edges
            .Select(edge => new GraphJsonEdge
            {
                Source = edge.SourceId,
                Target = edge.TargetId,
                Relation = edge.Relation,
                Relationship = edge.Relation,
                Confidence = edge.Confidence.ToString().ToUpperInvariant(),
                SourceFile = edge.SourceFile,
                Line = edge.Line,
                Weight = 1
            })
            .ToList();

        return new GraphJsonDocument
        {
            Directed = true,
            Multigraph = false,
            Graph = new GraphJsonMetadata
            {
                SolutionPath = snapshot.SolutionPath,
                BuiltAt = snapshot.BuiltAt,
                NodeCount = nodes.Count,
                EdgeCount = edges.Count,
                Generator = "Graphify.CSharp"
            },
            Nodes = nodes,
            Links = edges,
            Edges = edges,
            Metadata = new GraphJsonMetadata
            {
                SolutionPath = snapshot.SolutionPath,
                BuiltAt = snapshot.BuiltAt,
                NodeCount = nodes.Count,
                EdgeCount = edges.Count,
                Generator = "Graphify.CSharp"
            }
        };
    }
}

public sealed class GraphJsonDocument
{
    public bool Directed { get; init; }
    public bool Multigraph { get; init; }
    public GraphJsonMetadata? Graph { get; init; }
    public IReadOnlyList<GraphJsonNode> Nodes { get; init; } = [];
    public IReadOnlyList<GraphJsonEdge> Links { get; init; } = [];
    public IReadOnlyList<GraphJsonEdge> Edges { get; init; } = [];
    public GraphJsonMetadata? Metadata { get; init; }
}

public sealed class GraphJsonMetadata
{
    public string? SolutionPath { get; init; }
    public DateTimeOffset BuiltAt { get; init; }
    public int NodeCount { get; init; }
    public int EdgeCount { get; init; }
    public string? Generator { get; init; }
}

public sealed class GraphJsonNode
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string FileType { get; init; } = "code";
    public string? SourceFile { get; init; }
    public string? Type { get; init; }
    public string? Kind { get; init; }
    public string? FullName { get; init; }
    public string? Assembly { get; init; }
    public string? FilePath { get; init; }
    public int? Line { get; init; }
    public int? EndLine { get; init; }
}

public sealed class GraphJsonEdge
{
    public required string Source { get; init; }
    public required string Target { get; init; }
    public required string Relation { get; init; }
    public required string Relationship { get; init; }
    public required string Confidence { get; init; }
    public string? SourceFile { get; init; }
    public int? Line { get; init; }
    public int Weight { get; init; } = 1;
}
