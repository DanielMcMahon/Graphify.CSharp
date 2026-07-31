using System.Text;
using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Storage;

namespace Graphify.CSharp.Core.Query;

public sealed record ChangeDifficulty(string Level, string Rationale);

public sealed record SymbolRemovalAssessment(
    GraphNode Symbol,
    int DirectDependents,
    int TransitiveDependents,
    ChangeDifficulty Difficulty,
    IReadOnlyList<string> DependentLines,
    IReadOnlyList<string> OutgoingLines,
    string Markdown);

public sealed record MediatorReplacementAssessment(
    int DispatchSites,
    int PublishSites,
    int HandlerCount,
    int CommandCount,
    int NotificationCount,
    int FilesAffected,
    ChangeDifficulty Difficulty,
    IReadOnlyList<string> DispatchLines,
    IReadOnlyList<string> HandlerLines,
    IReadOnlyList<string> MigrationSteps,
    string Markdown);

public sealed class ArchitectureAssessmentService
{
    private static readonly string[] DependentRelations =
    [
        GraphRelation.Calls,
        GraphRelation.Dispatches,
        GraphRelation.Routes,
        GraphRelation.Handles,
        GraphRelation.Injects,
        GraphRelation.References,
        GraphRelation.Registers
    ];

    private static readonly string[] OutgoingRelations =
    [
        GraphRelation.Calls,
        GraphRelation.Dispatches,
        GraphRelation.Publishes,
        GraphRelation.Injects,
        GraphRelation.Handles,
        GraphRelation.Registers
    ];

    public async Task<SymbolRemovalAssessment?> TryAssessSymbolRemovalAsync(
        GraphDatabase database,
        string question,
        IReadOnlyList<GraphNode> candidates,
        CancellationToken cancellationToken = default)
    {
        if (!LooksLikeRemovalQuestion(question) || candidates.Count == 0)
        {
            return null;
        }

        return await AssessSymbolRemovalAsync(database, candidates[0], cancellationToken).ConfigureAwait(false);
    }

    public async Task<SymbolRemovalAssessment> AssessSymbolRemovalAsync(
        GraphDatabase database,
        GraphNode symbol,
        CancellationToken cancellationToken = default)
    {
        var direct = await CollectDependentsAsync(database, symbol.Id, maxDepth: 1, cancellationToken).ConfigureAwait(false);
        var transitive = await CollectDependentsAsync(database, symbol.Id, maxDepth: 3, cancellationToken).ConfigureAwait(false);
        var outgoing = await CollectOutgoingAsync(database, symbol.Id, cancellationToken).ConfigureAwait(false);
        var difficulty = ScoreRemovalDifficulty(direct.Count, transitive.Count, outgoing.Count);

        var builder = new StringBuilder();
        builder.AppendLine("## Removal / change impact");
        builder.AppendLine($"**{symbol.FullName ?? symbol.Name}** — difficulty: **{difficulty.Level}**");
        builder.AppendLine();
        builder.AppendLine(difficulty.Rationale);
        builder.AppendLine();
        builder.AppendLine($"- Direct dependents: {direct.Count}");
        builder.AppendLine($"- Transitive dependents (3 hops): {transitive.Count}");
        builder.AppendLine($"- Outgoing dependencies: {outgoing.Count}");
        builder.AppendLine();

        if (direct.Count > 0)
        {
            builder.AppendLine("### Who depends on this");
            foreach (var line in direct.Take(15))
            {
                builder.AppendLine($"- {line}");
            }

            builder.AppendLine();
        }

        if (outgoing.Count > 0)
        {
            builder.AppendLine("### What this depends on");
            foreach (var line in outgoing.Take(12))
            {
                builder.AppendLine($"- {line}");
            }

            builder.AppendLine();
        }

        builder.AppendLine("### What this means");
        builder.AppendLine(ExplainRemoval(symbol, direct.Count, transitive.Count, outgoing.Count, difficulty));

        return new SymbolRemovalAssessment(
            symbol,
            direct.Count,
            transitive.Count,
            difficulty,
            direct,
            outgoing,
            builder.ToString());
    }

    public async Task<MediatorReplacementAssessment> AssessMediatorReplacementAsync(
        GraphDatabase database,
        bool justMyCode = true,
        CancellationToken cancellationToken = default)
    {
        var footprint = await database.GetMediatorFootprintAsync(justMyCode, cancellationToken).ConfigureAwait(false);
        var difficulty = ScoreMediatorDifficulty(footprint);

        var builder = new StringBuilder();
        builder.AppendLine("## MediatR footprint");
        builder.AppendLine($"Swap difficulty: **{difficulty.Level}**");
        builder.AppendLine();
        builder.AppendLine(difficulty.Rationale);
        builder.AppendLine();
        builder.AppendLine($"- `Send` / `SendAsync` dispatch sites: {footprint.DispatchSites}");
        builder.AppendLine($"- `Publish` / `PublishAsync` sites: {footprint.PublishSites}");
        builder.AppendLine($"- Request handlers: {footprint.HandlerCount}");
        builder.AppendLine($"- Commands / requests touched: {footprint.CommandCount}");
        builder.AppendLine($"- Notifications touched: {footprint.NotificationCount}");
        builder.AppendLine($"- Source files involved: {footprint.FilesAffected}");
        builder.AppendLine();

        if (footprint.DispatchLines.Count > 0)
        {
            builder.AppendLine("### Dispatch sites (caller → command)");
            foreach (var line in footprint.DispatchLines.Take(20))
            {
                builder.AppendLine($"- {line}");
            }

            builder.AppendLine();
        }

        if (footprint.HandlerLines.Count > 0)
        {
            builder.AppendLine("### Handlers (handler → command/notification)");
            foreach (var line in footprint.HandlerLines.Take(20))
            {
                builder.AppendLine($"- {line}");
            }

            builder.AppendLine();
        }

        var steps = BuildMediatorMigrationSteps(footprint);
        builder.AppendLine("### If you swap MediatR for direct handler calls");
        foreach (var step in steps)
        {
            builder.AppendLine($"- {step}");
        }

        return new MediatorReplacementAssessment(
            footprint.DispatchSites,
            footprint.PublishSites,
            footprint.HandlerCount,
            footprint.CommandCount,
            footprint.NotificationCount,
            footprint.FilesAffected,
            difficulty,
            footprint.DispatchLines,
            footprint.HandlerLines,
            steps,
            builder.ToString());
    }

    public bool LooksLikeMediatorQuestion(string question) =>
        question.Contains("mediator", StringComparison.OrdinalIgnoreCase)
        || question.Contains("mediatr", StringComparison.OrdinalIgnoreCase)
        || question.Contains("isender", StringComparison.OrdinalIgnoreCase)
        || question.Contains("imediator", StringComparison.OrdinalIgnoreCase);

    public bool LooksLikeRemovalQuestion(string question)
    {
        var lower = question.ToLowerInvariant();
        return lower.Contains("remove ")
            || lower.Contains("take out")
            || lower.Contains("delete ")
            || lower.Contains("drop ")
            || lower.Contains("without ")
            || lower.Contains("what breaks")
            || lower.Contains("what would break")
            || lower.Contains("can i remove")
            || lower.Contains("swap ")
            || lower.Contains("replace ");
    }

    private static async Task<IReadOnlyList<string>> CollectDependentsAsync(
        GraphDatabase database,
        string nodeId,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { nodeId };
        var queue = new Queue<(string Id, int Depth)>();
        queue.Enqueue((nodeId, 0));

        while (queue.Count > 0)
        {
            var (currentId, depth) = queue.Dequeue();
            if (depth >= maxDepth)
            {
                continue;
            }

            var incoming = await database.GetIncomingEdgesAsync(currentId, cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var edge in incoming.Where(edge => DependentRelations.Contains(edge.Relation)))
            {
                if (!visited.Add(edge.SourceId))
                {
                    continue;
                }

                var caller = await database.GetNodeAsync(edge.SourceId, cancellationToken).ConfigureAwait(false);
                if (caller is null)
                {
                    continue;
                }

                var location = caller.FilePath is null ? string.Empty : $" ({caller.FilePath}:{caller.Line})";
                lines.Add($"{edge.Relation} <- {caller.FullName ?? caller.Name}{location}");
                queue.Enqueue((edge.SourceId, depth + 1));
            }
        }

        return lines;
    }

    private static async Task<IReadOnlyList<string>> CollectOutgoingAsync(
        GraphDatabase database,
        string nodeId,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        var outgoing = await database.GetOutgoingEdgesAsync(nodeId, cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach (var edge in outgoing.Where(edge => OutgoingRelations.Contains(edge.Relation)))
        {
            var target = await database.GetNodeAsync(edge.TargetId, cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                continue;
            }

            var location = target.FilePath is null ? string.Empty : $" ({target.FilePath}:{target.Line})";
            lines.Add($"{edge.Relation} -> {target.FullName ?? target.Name}{location}");
        }

        return lines;
    }

    private static ChangeDifficulty ScoreRemovalDifficulty(int direct, int transitive, int outgoing)
    {
        if (direct == 0 && outgoing <= 2)
        {
            return new ChangeDifficulty("Low", "Nothing in the graph depends on this symbol. It may be unused, test-only, or only reached indirectly.");
        }

        if (direct <= 3 && transitive <= 8)
        {
            return new ChangeDifficulty("Medium", "A small number of call sites depend on this. Expect localized edits and focused regression testing.");
        }

        return new ChangeDifficulty("High", "Many symbols depend on this directly or transitively. Removing or replacing it is a cross-cutting change.");
    }

    private static ChangeDifficulty ScoreMediatorDifficulty(MediatorFootprint footprint)
    {
        var touchpoints = footprint.DispatchSites + footprint.PublishSites + footprint.HandlerCount;
        if (touchpoints <= 8 && footprint.FilesAffected <= 6)
        {
            return new ChangeDifficulty("Low", "MediatR is used in a small, localized area. Replacing dispatch with direct handler calls is feasible project-by-project.");
        }

        if (touchpoints <= 40 && footprint.FilesAffected <= 25)
        {
            return new ChangeDifficulty("Medium", "MediatR is a meaningful part of the architecture. Plan a staged migration: endpoints first, then handlers, then DI registration.");
        }

        return new ChangeDifficulty("High", "MediatR is deeply embedded. A swap affects many dispatch sites and handlers across the solution.");
    }

    private static string ExplainRemoval(
        GraphNode symbol,
        int direct,
        int transitive,
        int outgoing,
        ChangeDifficulty difficulty)
    {
        if (direct == 0)
        {
            return $"The graph does not show callers for `{symbol.Name}`. It may be dead code, only used via reflection, or an interface with alternate implementations you still need elsewhere.";
        }

        if (outgoing > direct * 2)
        {
            return $"`{symbol.Name}` is a hub: many things depend on it, and it depends on many others. Changing its contract will ripple outward.";
        }

        return difficulty.Level switch
        {
            "Low" => "You can likely remove or replace this with limited blast radius. Confirm with tests around the listed dependents.",
            "Medium" => "Plan to update the listed dependents and run feature-level tests. The graph shows a bounded set of touchpoints.",
            _ => "Treat this as an architectural change. Map each dependent to a migration step before removing or swapping the symbol."
        };
    }

    private static IReadOnlyList<string> BuildMediatorMigrationSteps(MediatorFootprint footprint)
    {
        var steps = new List<string>
        {
            $"Inventory complete: {footprint.DispatchSites} dispatch sites, {footprint.HandlerCount} handlers, {footprint.FilesAffected} files.",
            "For each `dispatches` edge, replace `_mediator.Send(command)` with a direct call to the handler method (or an application service facade).",
            "Remove `IRequestHandler<T>` / `INotificationHandler<T>` registrations once callers invoke handlers directly.",
            "Collapse command/query DTOs only if they no longer add value without the pipeline behaviors MediatR provided (validation, logging, transactions).",
            "Run endpoint and handler tests per feature area; mediator swaps rarely fail at compile time but often break behavior wiring."
        };

        if (footprint.PublishSites > 0)
        {
            steps.Add($"Publishing is used ({footprint.PublishSites} sites). Decide whether notifications become direct calls, domain events, or an in-process event bus.");
        }

        return steps;
    }
}
