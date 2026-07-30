namespace Graphify.CSharp.Core.Models;

public sealed record GraphNode(
    string Id,
    NodeKind Kind,
    string Name,
    string? FullName,
    string? Assembly,
    string? FilePath,
    int? Line,
    int? EndLine,
    string? MetadataJson = null);
