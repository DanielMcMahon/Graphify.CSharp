using System.Text;
using System.Text.RegularExpressions;
using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Storage;
using Graphify.CSharp.Core.Workspace;

namespace Graphify.CSharp.Core.Query;

public sealed record InvestigationResult(
    string Topic,
    string Markdown,
    string? HandoffPath,
    IReadOnlyList<string> FilesToRead,
    IReadOnlyList<string> SuggestedFollowUps);

public sealed class InvestigationService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "how", "does", "do", "work", "works", "what", "is", "are", "the", "a", "an", "from", "to",
        "trace", "who", "calls", "when", "where", "why", "explain", "about", "for", "in", "of", "and"
    };

    private readonly HowDoesItWorkService _howService = new();
    private readonly GraphQueryService _queryService = new();
    private readonly GraphFlowService _flowService = new();
    private readonly ArchitectureAssessmentService _assessmentService = new();

    public async Task<InvestigationResult> InvestigateAsync(
        GraphDatabase database,
        string question,
        string? projectRoot = null,
        bool writeHandoff = true,
        CancellationToken cancellationToken = default)
    {
        var terms = ExtractSearchTerms(question).ToList();
        var candidates = await FindCandidatesAsync(database, terms, cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            var empty = $"""No symbols matched "{question}". Try a class, method, handler, or endpoint name.""";
            return new InvestigationResult(question, empty, null, [], []);
        }

        var primary = candidates[0];
        var explanation = await _howService.ExplainAsync(database, primary.FullName ?? primary.Name, cancellationToken).ConfigureAwait(false);
        var impact = await AnalyzeImpactAsync(database, primary, cancellationToken).ConfigureAwait(false);
        var flows = await _flowService.FindEndpointFlowsAsync(database, question, cancellationToken: cancellationToken).ConfigureAwait(false);
        var files = CollectFiles(candidates, impact);
        var followUps = BuildFollowUps(primary, candidates, impact, flows);

        var builder = new StringBuilder();
        builder.AppendLine($"# Investigation: {question}");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine(Summarize(primary, explanation, flows));
        builder.AppendLine();
        builder.AppendLine(explanation);
        builder.AppendLine();

        if (impact.Count > 0)
        {
            builder.AppendLine("## Impact (who depends on this)");
            foreach (var line in impact.Take(20))
            {
                builder.AppendLine($"- {line}");
            }

            builder.AppendLine();
        }

        if (candidates.Count > 1)
        {
            builder.AppendLine("## Related symbols");
            foreach (var candidate in candidates.Skip(1).Take(8))
            {
                builder.AppendLine($"- {candidate.Kind}: {candidate.FullName ?? candidate.Name}");
            }

            builder.AppendLine();
        }

        if (followUps.Count > 0)
        {
            builder.AppendLine("## Suggested follow-ups");
            foreach (var followUp in followUps)
            {
                builder.AppendLine($"- {followUp}");
            }

            builder.AppendLine();
        }

        if (files.Count > 0)
        {
            builder.AppendLine("## Files to read");
            foreach (var file in files)
            {
                builder.AppendLine($"- {file}");
            }

            builder.AppendLine();
        }

        if (_assessmentService.LooksLikeMediatorQuestion(question))
        {
            var mediator = await _assessmentService.AssessMediatorReplacementAsync(database, justMyCode: true, cancellationToken).ConfigureAwait(false);
            builder.AppendLine(mediator.Markdown);
            followUps = followUps.Concat(["List every MediatR dispatch site by feature", "Which handlers have no direct caller after removing MediatR?"]).ToList();
        }

        var removal = await _assessmentService.TryAssessSymbolRemovalAsync(database, question, candidates, cancellationToken).ConfigureAwait(false);
        if (removal is not null && !_assessmentService.LooksLikeMediatorQuestion(question))
        {
            builder.AppendLine(removal.Markdown);
        }

        var markdown = builder.ToString();
        string? handoffPath = null;
        if (writeHandoff && !string.IsNullOrWhiteSpace(projectRoot))
        {
            handoffPath = WriteHandoff(projectRoot, question, markdown);
        }

        return new InvestigationResult(question, markdown, handoffPath, files, followUps);
    }

    private static IEnumerable<string> ExtractSearchTerms(string question)
    {
        var terms = new List<string> { question.Trim() };
        foreach (var token in Regex.Split(question, @"[^A-Za-z0-9_]+"))
        {
            if (token.Length < 3 || StopWords.Contains(token))
            {
                continue;
            }

            terms.Add(token);
            if (token.Length > 3)
            {
                terms.Add(char.ToUpperInvariant(token[0]) + token[1..]);
            }
        }

        return terms.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<List<GraphNode>> FindCandidatesAsync(
        GraphDatabase database,
        IReadOnlyList<string> terms,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<GraphNode>();

        foreach (var term in terms)
        {
            var matches = await database.SearchNodesAsync(term, limit: 8, justMyCode: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var match in matches)
            {
                if (seen.Add(match.Id))
                {
                    results.Add(match);
                }
            }
        }

        return results
            .OrderBy(node => node.Kind == NodeKind.Method ? 0 : node.Kind == NodeKind.Type ? 1 : 2)
            .ThenBy(node => node.FullName?.Length ?? node.Name.Length)
            .ToList();
    }

    private async Task<IReadOnlyList<string>> AnalyzeImpactAsync(
        GraphDatabase database,
        GraphNode primary,
        CancellationToken cancellationToken,
        int maxDepth = 2)
    {
        var lines = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { primary.Id };
        var queue = new Queue<(string NodeId, int Depth)>();
        queue.Enqueue((primary.Id, 0));

        while (queue.Count > 0)
        {
            var (nodeId, depth) = queue.Dequeue();
            if (depth >= maxDepth)
            {
                continue;
            }

            var incoming = await database.GetIncomingEdgesAsync(nodeId, cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var edge in incoming.Where(edge => edge.Relation is GraphRelation.Calls or GraphRelation.Dispatches or GraphRelation.Routes or GraphRelation.Handles))
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

                lines.Add($"{edge.Relation} <- {caller.FullName ?? caller.Name} (depth {depth + 1})");
                queue.Enqueue((edge.SourceId, depth + 1));
            }
        }

        return lines;
    }

    private static IReadOnlyList<string> CollectFiles(IReadOnlyList<GraphNode> candidates, IReadOnlyList<string> impactLines)
    {
        return candidates
            .Where(node => !string.IsNullOrWhiteSpace(node.FilePath))
            .Select(node => $"{node.FilePath}:{node.Line}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static IReadOnlyList<string> BuildFollowUps(
        GraphNode primary,
        IReadOnlyList<GraphNode> candidates,
        IReadOnlyList<string> impact,
        IReadOnlyList<GraphPath> flows)
    {
        var followUps = new List<string>
        {
            $"Explain `{primary.Name}` in more detail",
            $"Find path from an API entry point to `{primary.Name}`"
        };

        if (impact.Count > 0)
        {
            followUps.Add($"What breaks if `{primary.Name}` changes?");
        }

        if (flows.Count > 0)
        {
            followUps.Add("Trace the full endpoint-to-database flow");
        }

        if (candidates.Count > 1)
        {
            followUps.Add($"Compare `{primary.Name}` with `{candidates[1].Name}`");
        }

        return followUps;
    }

    private static string Summarize(GraphNode primary, string explanation, IReadOnlyList<GraphPath> flows)
    {
        var kind = primary.Kind.ToString().ToLowerInvariant();
        var flowHint = flows.Count > 0 ? " It participates in at least one HTTP-to-repository flow." : string.Empty;
        return $"Primary match: **{primary.FullName ?? primary.Name}** ({kind} in {primary.Assembly}).{flowHint} See sections below for callers, dependencies, and source files.";
    }

    private static string WriteHandoff(string projectRoot, string question, string markdown)
    {
        var directory = Path.Combine(projectRoot, GraphWorkspace.ConfigDirectory, "investigations");
        Directory.CreateDirectory(directory);
        var slug = Slugify(question);
        var path = Path.Combine(directory, $"{slug}.md");
        File.WriteAllText(path, markdown);
        return path;
    }

    private static string Slugify(string text)
    {
        var slug = Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "investigation" : slug[..Math.Min(slug.Length, 80)];
    }
}
