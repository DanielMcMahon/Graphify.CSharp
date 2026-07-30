namespace Graphify.CSharp.Core.Models;

public sealed class GraphSnapshot
{
    public required string SolutionPath { get; init; }
    public required DateTimeOffset BuiltAt { get; init; }
    public required IReadOnlyList<GraphNode> Nodes { get; init; }
    public required IReadOnlyList<GraphEdge> Edges { get; init; }
    public IReadOnlyList<string> UserAssemblies { get; init; } = [];
}
