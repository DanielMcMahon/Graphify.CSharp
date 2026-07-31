using System.Text;
using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Storage;

namespace Graphify.CSharp.Core.Query;

public sealed class HowDoesItWorkService
{
    private readonly GraphQueryService _queryService = new();
    private readonly GraphFlowService _flowService = new();

    public async Task<string> ExplainAsync(
        GraphDatabase database,
        string topic,
        CancellationToken cancellationToken = default)
    {
        var matches = await database.SearchNodesAsync(topic, limit: 5, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (matches.Count == 0)
        {
            return $"""No symbols matched "{topic}". Try a class name, method name, or endpoint route fragment.""";
        }

        var primary = matches[0];
        var explanation = await _queryService.ExplainAsync(database, primary.FullName ?? primary.Name, cancellationToken).ConfigureAwait(false);
        var flows = await _flowService.FindEndpointFlowsAsync(database, topic, cancellationToken: cancellationToken).ConfigureAwait(false);

        var builder = new StringBuilder();
        builder.AppendLine($"# How {primary.FullName ?? primary.Name} works");
        builder.AppendLine();
        builder.AppendLine(FormatNodeSummary(primary));

        if (matches.Count > 1)
        {
            builder.AppendLine("Other matches:");
            foreach (var match in matches.Skip(1))
            {
                builder.AppendLine($"- {match.Kind}: {match.FullName ?? match.Name}");
            }

            builder.AppendLine();
        }

        if (explanation.Node is null)
        {
            return builder.ToString();
        }

        builder.AppendLine("## Role");
        builder.AppendLine(DescribeRole(explanation.Node, explanation.Incoming, explanation.Outgoing));
        builder.AppendLine();

        var incomingCalls = await ExpandEdgesAsync(database, explanation.Incoming, isIncoming: true, cancellationToken).ConfigureAwait(false);
        if (incomingCalls.Count > 0)
        {
            builder.AppendLine("## Who uses it");
            foreach (var line in incomingCalls)
            {
                builder.AppendLine($"- {line}");
            }

            builder.AppendLine();
        }

        var outgoingCalls = await ExpandEdgesAsync(database, explanation.Outgoing, isIncoming: false, cancellationToken).ConfigureAwait(false);
        if (outgoingCalls.Count > 0)
        {
            builder.AppendLine("## What it depends on");
            foreach (var line in outgoingCalls)
            {
                builder.AppendLine($"- {line}");
            }

            builder.AppendLine();
        }

        var mediatorEdges = explanation.Outgoing
            .Concat(explanation.Incoming)
            .Where(edge => edge.Relation is GraphRelation.Dispatches or GraphRelation.Handles or GraphRelation.Publishes)
            .ToList();
        if (mediatorEdges.Count > 0)
        {
            builder.AppendLine("## MediatR wiring");
            foreach (var edge in mediatorEdges.Take(12))
            {
                var otherId = edge.SourceId == explanation.Node.Id ? edge.TargetId : edge.SourceId;
                var other = await database.GetNodeAsync(otherId, cancellationToken).ConfigureAwait(false);
                var direction = edge.SourceId == explanation.Node.Id ? "->" : "<-";
                builder.AppendLine($"- {edge.Relation} {direction} {other?.FullName ?? otherId} ({edge.Confidence})");
            }

            builder.AppendLine();
        }

        if (flows.Count > 0)
        {
            builder.AppendLine("## Endpoint flow");
            foreach (var flow in flows.Take(3))
            {
                builder.AppendLine(FormatPath(flow));
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Source");
        if (!string.IsNullOrWhiteSpace(primary.FilePath))
        {
            builder.AppendLine($"{primary.FilePath}:{primary.Line}");
        }

        return builder.ToString();
    }

    private static string FormatNodeSummary(GraphNode node)
    {
        var location = string.IsNullOrWhiteSpace(node.FilePath)
            ? string.Empty
            : $" at {node.FilePath}:{node.Line}";
        return $"{node.Kind} in {node.Assembly}{location}";
    }

    private static string DescribeRole(GraphNode node, IReadOnlyList<GraphEdge> incoming, IReadOnlyList<GraphEdge> outgoing)
    {
        if (incoming.Any(edge => edge.Relation == GraphRelation.Routes))
        {
            return "This looks like an HTTP entry point. It receives requests and delegates through handlers and services.";
        }

        if (outgoing.Any(edge => edge.Relation == GraphRelation.Dispatches) || incoming.Any(edge => edge.Relation == GraphRelation.Handles))
        {
            return "This participates in MediatR request/notification handling.";
        }

        if (node.Name.EndsWith("Repository", StringComparison.Ordinal) || (node.FullName?.Contains("Repository", StringComparison.Ordinal) ?? false))
        {
            return "This is a data-access component. Callers typically reach it through services or handlers.";
        }

        if (node.Name.EndsWith("Handler", StringComparison.Ordinal))
        {
            return "This is an application handler that coordinates domain logic and infrastructure.";
        }

        if (node.Name.EndsWith("Service", StringComparison.Ordinal))
        {
            return "This is an application service used by controllers, handlers, or other services.";
        }

        return "This symbol sits in the architecture graph between its callers and dependencies.";
    }

    private static async Task<IReadOnlyList<string>> ExpandEdgesAsync(
        GraphDatabase database,
        IReadOnlyList<GraphEdge> edges,
        bool isIncoming,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        var priorityRelations = new[]
        {
            GraphRelation.Routes,
            GraphRelation.Calls,
            GraphRelation.Dispatches,
            GraphRelation.Handles,
            GraphRelation.Injects,
            GraphRelation.References
        };

        foreach (var relation in priorityRelations)
        {
            foreach (var edge in edges.Where(edge => edge.Relation == relation).Take(8))
            {
                var otherId = isIncoming ? edge.SourceId : edge.TargetId;
                var other = await database.GetNodeAsync(otherId, cancellationToken).ConfigureAwait(false);
                lines.Add($"{edge.Relation} {(isIncoming ? "from" : "to")} {other?.FullName ?? otherId} ({edge.Confidence})");
            }
        }

        return lines;
    }

    private static string FormatPath(GraphPath path)
    {
        var builder = new StringBuilder();
        foreach (var step in path.Steps)
        {
            if (step.IncomingEdge is null)
            {
                builder.Append(step.Node.FullName ?? step.Node.Name);
                continue;
            }

            builder.Append($" --[{step.IncomingEdge.Relation}]--> {step.Node.FullName ?? step.Node.Name}");
        }

        return builder.ToString();
    }
}
