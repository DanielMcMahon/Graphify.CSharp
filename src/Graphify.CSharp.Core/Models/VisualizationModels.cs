namespace Graphify.CSharp.Core.Models;

public sealed record LinkedEdge(
    GraphEdge Edge,
    GraphNode OtherNode,
    bool IsIncoming);

public sealed record NodeDetail(
    GraphNode Node,
    IReadOnlyList<LinkedEdge> Callers,
    IReadOnlyList<LinkedEdge> Callees,
    IReadOnlyList<LinkedEdge> ReferencesIn,
    IReadOnlyList<LinkedEdge> ReferencesOut,
    IReadOnlyList<LinkedEdge> OtherIncoming,
    IReadOnlyList<LinkedEdge> OtherOutgoing);

public sealed record GraphExport(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record GraphOverview(
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyDictionary<string, int> NodeCountsByKind,
    IReadOnlyDictionary<string, int> EdgeCountsByRelation,
    IReadOnlyList<GraphNode> SeedNodes);
