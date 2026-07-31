using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Storage;

namespace Graphify.CSharp.Core.Query;

public sealed class StartupCompositionService
{
    public async Task<StartupCompositionResult> GetStartupMapAsync(
        GraphDatabase database,
        string? programFile = null,
        int depth = 4,
        int maxNodes = 250,
        bool justMyCode = true,
        CancellationToken cancellationToken = default)
    {
        var entryPoints = await database.FindStartupEntryPointsAsync(justMyCode, programFile, cancellationToken)
            .ConfigureAwait(false);
        if (entryPoints.Count == 0)
        {
            return new StartupCompositionResult([], new GraphExport([], [], await database.GetMetadataAsync(cancellationToken).ConfigureAwait(false)));
        }

        var export = await database.GetCompositionSubgraphAsync(
            entryPoints.Select(node => node.Id).Distinct(StringComparer.Ordinal).ToList(),
            depth,
            maxNodes,
            GraphDatabase.CompositionRelations,
            justMyCode,
            cancellationToken).ConfigureAwait(false);

        return new StartupCompositionResult(entryPoints, export);
    }
}

public sealed record StartupCompositionResult(
    IReadOnlyList<GraphNode> EntryPoints,
    GraphExport Graph);
