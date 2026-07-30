namespace Graphify.CSharp.Core.Models;

public sealed record GraphEdge(
    string SourceId,
    string TargetId,
    string Relation,
    GraphConfidence Confidence,
    string? SourceFile,
    int? Line,
    string? MetadataJson = null);
