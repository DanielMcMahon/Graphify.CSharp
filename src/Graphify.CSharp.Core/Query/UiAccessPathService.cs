using System.Text;
using System.Text.Json;
using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Storage;

namespace Graphify.CSharp.Core.Query;

public sealed record UiSelectorHint(string Kind, string Value, string Confidence);

public sealed record UiGateInfo(string Expression, string GateKind, IReadOnlyList<string> Bindings);

public sealed record UiAccessPathResult(
    string Query,
    GraphNode? Target,
    GraphNode? Surface,
    GraphNode? Page,
    IReadOnlyList<UiGateInfo> Gates,
    IReadOnlyList<UiSelectorHint> Selectors,
    IReadOnlyList<string> NavigationSteps,
    IReadOnlyList<string> Prerequisites,
    string Markdown,
    string Json);

public sealed record UiSurfaceMapResult(
    GraphNode Surface,
    GraphNode? Page,
    IReadOnlyList<GraphNode> Fragments,
    IReadOnlyList<GraphNode> Elements,
    IReadOnlyList<GraphNode> Gates,
    IReadOnlyList<GraphNode> Actions,
    string Markdown);

public sealed class UiAccessPathService
{
    public async Task<UiAccessPathResult> GetAccessPathAsync(
        GraphDatabase database,
        string elementQuery,
        string? surfaceQuery = null,
        CancellationToken cancellationToken = default)
    {
        var target = await ResolveTargetAsync(database, elementQuery, surfaceQuery, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            var emptyMarkdown = $"""No UI element, fragment, or surface matched "{elementQuery}".""";
            return new UiAccessPathResult(
                elementQuery,
                null,
                null,
                null,
                [],
                [],
                [],
                [],
                emptyMarkdown,
                JsonSerializer.Serialize(new { query = elementQuery, found = false }));
        }

        var surface = await FindOwningSurfaceAsync(database, target, cancellationToken).ConfigureAwait(false);
        var page = surface is null ? null : await FindHostingPageAsync(database, surface.Id, cancellationToken).ConfigureAwait(false);
        var gates = await CollectGatesAsync(database, target.Id, cancellationToken).ConfigureAwait(false);
        var selectors = await CollectSelectorsAsync(database, target.Id, cancellationToken).ConfigureAwait(false);
        var navigation = await CollectNavigationAsync(database, surface?.Id, cancellationToken).ConfigureAwait(false);
        var prerequisites = BuildPrerequisites(gates, surface, page);

        var markdown = BuildAccessPathMarkdown(elementQuery, target, surface, page, gates, selectors, navigation, prerequisites);
        var json = BuildAccessPathJson(elementQuery, target, surface, page, gates, selectors, navigation, prerequisites);

        return new UiAccessPathResult(
            elementQuery,
            target,
            surface,
            page,
            gates,
            selectors,
            navigation,
            prerequisites,
            markdown,
            json);
    }

    public async Task<UiSurfaceMapResult> GetSurfaceMapAsync(
        GraphDatabase database,
        string surfaceQuery,
        CancellationToken cancellationToken = default)
    {
        var surface = await database.FindUiSurfaceAsync(surfaceQuery, cancellationToken).ConfigureAwait(false)
            ?? (await database.SearchUiNodesAsync(surfaceQuery, limit: 1, cancellationToken).ConfigureAwait(false)).FirstOrDefault();

        if (surface is null || surface.Kind != NodeKind.UiSurface)
        {
            throw new InvalidOperationException($"No UI surface matched '{surfaceQuery}'.");
        }

        var page = await FindHostingPageAsync(database, surface.Id, cancellationToken).ConfigureAwait(false);
        var fragments = await GetRenderedNodesAsync(database, surface.Id, NodeKind.UiFragment, cancellationToken).ConfigureAwait(false);
        var elements = await GetRenderedNodesAsync(database, surface.Id, NodeKind.UiElement, cancellationToken).ConfigureAwait(false);
        var gates = await GetRelatedByKindAsync(database, surface.Id, NodeKind.UiGate, cancellationToken).ConfigureAwait(false);
        var actions = await GetRelatedByKindAsync(database, surface.Id, NodeKind.UiAction, cancellationToken).ConfigureAwait(false);

        var markdown = BuildSurfaceMapMarkdown(surface, page, fragments, elements, gates, actions);
        return new UiSurfaceMapResult(surface, page, fragments, elements, gates, actions, markdown);
    }

    public Task<string> ExportPrerequisitesJsonAsync(
        GraphDatabase database,
        string elementQuery,
        string? surfaceQuery = null,
        CancellationToken cancellationToken = default) =>
        GetAccessPathAsync(database, elementQuery, surfaceQuery, cancellationToken)
            .ContinueWith(task => task.Result.Json, cancellationToken);

    private static async Task<GraphNode?> ResolveTargetAsync(
        GraphDatabase database,
        string elementQuery,
        string? surfaceQuery,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(surfaceQuery))
        {
            var surface = await database.FindUiSurfaceAsync(surfaceQuery, cancellationToken).ConfigureAwait(false);
            if (surface is not null)
            {
                var rendered = await GetRenderedNodesAsync(database, surface.Id, null, cancellationToken).ConfigureAwait(false);
                var match = rendered.FirstOrDefault(node =>
                    node.Name.Contains(elementQuery, StringComparison.OrdinalIgnoreCase)
                    || (node.FullName?.Contains(elementQuery, StringComparison.OrdinalIgnoreCase) ?? false));
                if (match is not null)
                {
                    return match;
                }
            }
        }

        var candidates = await database.SearchUiNodesAsync(elementQuery, limit: 10, cancellationToken).ConfigureAwait(false);
        return candidates.FirstOrDefault(node => node.Kind is NodeKind.UiElement or NodeKind.UiFragment)
               ?? candidates.FirstOrDefault(node => node.Kind == NodeKind.UiSurface);
    }

    private static async Task<GraphNode?> FindOwningSurfaceAsync(
        GraphDatabase database,
        GraphNode target,
        CancellationToken cancellationToken)
    {
        if (target.Kind == NodeKind.UiSurface)
        {
            return target;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(target.Id);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (!visited.Add(nodeId))
            {
                continue;
            }

            var incoming = await database.GetIncomingEdgesAsync(nodeId, GraphRelation.Contains, cancellationToken).ConfigureAwait(false);
            incoming = incoming.Concat(await database.GetIncomingEdgesAsync(nodeId, GraphRelation.Renders, cancellationToken).ConfigureAwait(false)).ToList();

            foreach (var edge in incoming)
            {
                var parent = await database.GetNodeAsync(edge.SourceId, cancellationToken).ConfigureAwait(false);
                if (parent is null)
                {
                    continue;
                }

                if (parent.Kind == NodeKind.UiSurface)
                {
                    return parent;
                }

                queue.Enqueue(parent.Id);
            }
        }

        if (!string.IsNullOrWhiteSpace(target.Id) && target.Id.Contains("ui:surface|", StringComparison.Ordinal))
        {
            return target;
        }

        var surfaces = await database.SearchUiNodesAsync(target.Name, limit: 5, cancellationToken).ConfigureAwait(false);
        return surfaces.FirstOrDefault(node => node.Kind == NodeKind.UiSurface);
    }

    private static async Task<GraphNode?> FindHostingPageAsync(
        GraphDatabase database,
        string surfaceId,
        CancellationToken cancellationToken)
    {
        var incoming = await database.GetIncomingEdgesAsync(surfaceId, GraphRelation.Hosts, cancellationToken).ConfigureAwait(false);
        foreach (var edge in incoming)
        {
            var page = await database.GetNodeAsync(edge.SourceId, cancellationToken).ConfigureAwait(false);
            if (page?.Kind == NodeKind.Page)
            {
                return page;
            }
        }

        return null;
    }

    private static async Task<IReadOnlyList<UiGateInfo>> CollectGatesAsync(
        GraphDatabase database,
        string targetId,
        CancellationToken cancellationToken)
    {
        var gateIds = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(targetId);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            var outgoing = await database.GetOutgoingEdgesAsync(nodeId, GraphRelation.GatedBy, cancellationToken).ConfigureAwait(false);
            foreach (var edge in outgoing)
            {
                if (gateIds.Add(edge.TargetId))
                {
                    queue.Enqueue(edge.TargetId);
                }
            }
        }

        var gates = new List<UiGateInfo>();
        foreach (var gateId in gateIds)
        {
            var gateNode = await database.GetNodeAsync(gateId, cancellationToken).ConfigureAwait(false);
            if (gateNode?.Kind != NodeKind.UiGate)
            {
                continue;
            }

            var bindings = await GetBoundSymbolsAsync(database, gateId, cancellationToken).ConfigureAwait(false);
            var gateKind = gateNode.Name;
            gates.Add(new UiGateInfo(gateNode.FullName ?? gateNode.Name, gateKind, bindings));
        }

        return gates;
    }

    private static async Task<IReadOnlyList<string>> GetBoundSymbolsAsync(
        GraphDatabase database,
        string gateId,
        CancellationToken cancellationToken)
    {
        var edges = await database.GetOutgoingEdgesAsync(gateId, GraphRelation.BoundTo, cancellationToken).ConfigureAwait(false);
        var bindings = new List<string>();
        foreach (var edge in edges)
        {
            var binding = await database.GetNodeAsync(edge.TargetId, cancellationToken).ConfigureAwait(false);
            if (binding is not null)
            {
                bindings.Add(binding.FullName ?? binding.Name);
            }
        }

        return bindings;
    }

    private static async Task<IReadOnlyList<UiSelectorHint>> CollectSelectorsAsync(
        GraphDatabase database,
        string targetId,
        CancellationToken cancellationToken)
    {
        var edges = await database.GetOutgoingEdgesAsync(targetId, GraphRelation.EmitsSelector, cancellationToken).ConfigureAwait(false);
        var selectors = new List<UiSelectorHint>();
        foreach (var edge in edges)
        {
            var selector = await database.GetNodeAsync(edge.TargetId, cancellationToken).ConfigureAwait(false);
            if (selector is null)
            {
                continue;
            }

            selectors.Add(new UiSelectorHint(
                ParseSelectorKind(selector.FullName),
                selector.Name,
                edge.Confidence.ToString()));
        }

        return selectors;
    }

    private static async Task<IReadOnlyList<string>> CollectNavigationAsync(
        GraphDatabase database,
        string? surfaceId,
        CancellationToken cancellationToken)
    {
        if (surfaceId is null)
        {
            return [];
        }

        var steps = new List<string>();
        var incoming = await database.GetIncomingEdgesAsync(surfaceId, GraphRelation.NavigatesTo, cancellationToken).ConfigureAwait(false);
        foreach (var edge in incoming)
        {
            var action = await database.GetNodeAsync(edge.SourceId, cancellationToken).ConfigureAwait(false);
            if (action is not null)
            {
                steps.Add($"Action `{action.Name}` -> `{action.FullName ?? surfaceId}`");
            }
        }

        return steps;
    }

    private static IReadOnlyList<string> BuildPrerequisites(
        IReadOnlyList<UiGateInfo> gates,
        GraphNode? surface,
        GraphNode? page)
    {
        var prerequisites = new List<string>();
        if (page is not null)
        {
            prerequisites.Add($"Navigate to page `{page.Name}` ({page.FullName})");
        }
        else if (surface is not null)
        {
            prerequisites.Add($"Open UI surface `{surface.FullName ?? surface.Name}`");
        }

        foreach (var gate in gates)
        {
            prerequisites.Add($"Gate ({gate.GateKind}): {gate.Expression}");
            foreach (var binding in gate.Bindings)
            {
                prerequisites.Add($"  depends on `{binding}`");
            }
        }

        return prerequisites;
    }

    private static async Task<IReadOnlyList<GraphNode>> GetRenderedNodesAsync(
        GraphDatabase database,
        string parentId,
        NodeKind? kind,
        CancellationToken cancellationToken)
    {
        var edges = await database.GetOutgoingEdgesAsync(parentId, GraphRelation.Renders, cancellationToken).ConfigureAwait(false);
        edges = edges.Concat(await database.GetOutgoingEdgesAsync(parentId, GraphRelation.Contains, cancellationToken).ConfigureAwait(false)).ToList();

        var nodes = new List<GraphNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!seen.Add(edge.TargetId))
            {
                continue;
            }

            var node = await database.GetNodeAsync(edge.TargetId, cancellationToken).ConfigureAwait(false);
            if (node is null || kind is not null && node.Kind != kind)
            {
                continue;
            }

            nodes.Add(node);
        }

        return nodes;
    }

    private static async Task<IReadOnlyList<GraphNode>> GetRelatedByKindAsync(
        GraphDatabase database,
        string surfaceId,
        NodeKind kind,
        CancellationToken cancellationToken)
    {
        var rendered = await GetRenderedNodesAsync(database, surfaceId, null, cancellationToken).ConfigureAwait(false);
        return rendered.Where(node => node.Kind == kind).ToList();
    }

    private static string BuildAccessPathMarkdown(
        string query,
        GraphNode target,
        GraphNode? surface,
        GraphNode? page,
        IReadOnlyList<UiGateInfo> gates,
        IReadOnlyList<UiSelectorHint> selectors,
        IReadOnlyList<string> navigation,
        IReadOnlyList<string> prerequisites)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# UI access path: {query}");
        builder.AppendLine();
        builder.AppendLine("## Target");
        builder.AppendLine($"- Kind: {target.Kind}");
        builder.AppendLine($"- Name: {target.FullName ?? target.Name}");
        if (!string.IsNullOrWhiteSpace(target.FilePath))
        {
            builder.AppendLine($"- Source: {target.FilePath}:{target.Line}");
        }

        builder.AppendLine();
        builder.AppendLine("## Surface");
        builder.AppendLine(surface is null
            ? "- Not resolved"
            : $"- {surface.FullName ?? surface.Name} ({surface.FilePath}:{surface.Line})");

        if (page is not null)
        {
            builder.AppendLine();
            builder.AppendLine("## Page");
            builder.AppendLine($"- {page.FullName ?? page.Name}");
        }

        builder.AppendLine();
        builder.AppendLine("## Prerequisites");
        if (prerequisites.Count == 0)
        {
            builder.AppendLine("- None inferred");
        }
        else
        {
            foreach (var prerequisite in prerequisites)
            {
                builder.AppendLine($"- {prerequisite}");
            }
        }

        if (gates.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Visibility gates");
            foreach (var gate in gates)
            {
                builder.AppendLine($"- **{gate.GateKind}**: `{gate.Expression}`");
                foreach (var binding in gate.Bindings)
                {
                    builder.AppendLine($"  - binding: `{binding}`");
                }
            }
        }

        if (selectors.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Selector hints");
            foreach (var selector in selectors)
            {
                builder.AppendLine($"- {selector.Kind} (`{selector.Confidence}`): `{selector.Value}`");
            }
        }

        if (navigation.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Navigation");
            foreach (var step in navigation)
            {
                builder.AppendLine($"- {step}");
            }
        }

        return builder.ToString();
    }

    private static string BuildAccessPathJson(
        string query,
        GraphNode target,
        GraphNode? surface,
        GraphNode? page,
        IReadOnlyList<UiGateInfo> gates,
        IReadOnlyList<UiSelectorHint> selectors,
        IReadOnlyList<string> navigation,
        IReadOnlyList<string> prerequisites) =>
        JsonSerializer.Serialize(new
        {
            query,
            found = true,
            target = new
            {
                target.Kind,
                target.Name,
                target.FullName,
                target.FilePath,
                target.Line
            },
            surface = surface is null ? null : new { surface.Name, surface.FullName, surface.FilePath },
            page = page is null ? null : new { page.Name, page.FullName, page.FilePath },
            prerequisites,
            gates = gates.Select(gate => new { gate.GateKind, gate.Expression, gate.Bindings }),
            selectors = selectors.Select(selector => new { selector.Kind, selector.Value, selector.Confidence }),
            navigation
        }, new JsonSerializerOptions { WriteIndented = true });

    private static string BuildSurfaceMapMarkdown(
        GraphNode surface,
        GraphNode? page,
        IReadOnlyList<GraphNode> fragments,
        IReadOnlyList<GraphNode> elements,
        IReadOnlyList<GraphNode> gates,
        IReadOnlyList<GraphNode> actions)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# UI surface map: {surface.FullName ?? surface.Name}");
        builder.AppendLine();
        builder.AppendLine($"Source: {surface.FilePath}:{surface.Line}");
        if (page is not null)
        {
            builder.AppendLine($"Page: {page.FullName ?? page.Name}");
        }

        AppendNodeSection(builder, "Fragments", fragments);
        AppendNodeSection(builder, "Elements", elements);
        AppendNodeSection(builder, "Gates", gates);
        AppendNodeSection(builder, "Actions", actions);
        return builder.ToString();
    }

    private static void AppendNodeSection(StringBuilder builder, string title, IReadOnlyList<GraphNode> nodes)
    {
        builder.AppendLine();
        builder.AppendLine($"## {title}");
        if (nodes.Count == 0)
        {
            builder.AppendLine("- None");
            return;
        }

        foreach (var node in nodes)
        {
            builder.AppendLine($"- {node.Kind}: {node.FullName ?? node.Name} ({node.FilePath}:{node.Line})");
        }
    }

    private static string ParseSelectorKind(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return "unknown";
        }

        var separator = fullName.IndexOf(':');
        return separator > 0 ? fullName[..separator] : fullName;
    }
}
