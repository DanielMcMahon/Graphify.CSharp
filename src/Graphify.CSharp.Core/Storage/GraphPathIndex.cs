using Graphify.CSharp.Core.Models;

namespace Graphify.CSharp.Core.Storage;

public sealed class GraphPathIndex
{
    private readonly Dictionary<string, List<(string NeighborId, GraphEdge Edge)>> _adjacency;
    private readonly Dictionary<string, GraphNode> _nodes;

    public GraphPathIndex(
        IReadOnlyDictionary<string, List<(string NeighborId, GraphEdge Edge)>> adjacency,
        IReadOnlyDictionary<string, GraphNode> nodes)
    {
        _adjacency = new Dictionary<string, List<(string, GraphEdge)>>(adjacency, StringComparer.Ordinal);
        _nodes = new Dictionary<string, GraphNode>(nodes, StringComparer.Ordinal);
    }

    public IReadOnlyList<(string NeighborId, GraphEdge Edge)> GetNeighbors(string nodeId) =>
        _adjacency.TryGetValue(nodeId, out var neighbors) ? neighbors : [];

    public GraphNode? GetNode(string nodeId) =>
        _nodes.TryGetValue(nodeId, out var node) ? node : null;
}
