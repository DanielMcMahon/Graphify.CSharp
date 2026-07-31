using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Query;
using Graphify.CSharp.Core.Storage;
using Graphify.CSharp.Roslyn;

namespace Graphify.CSharp.Tests;

public class ArchitectureAssessmentServiceTests
{
    private static string SampleSolutionPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "SampleApp", "SampleApp.sln"));

    [Fact]
    public async Task AssessMediatorReplacement_reports_dispatch_and_handler_inventory()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"graphify-assess-{Guid.NewGuid():N}.db");
        var builder = new RoslynGraphBuilder();
        var snapshot = await builder.BuildAsync(SampleSolutionPath);

        await using var database = await GraphDatabase.OpenAsync(databasePath);
        await database.ReplaceSnapshotAsync(snapshot);

        var service = new ArchitectureAssessmentService();
        var result = await service.AssessMediatorReplacementAsync(database);

        Assert.Contains("MediatR footprint", result.Markdown, StringComparison.Ordinal);
        Assert.NotEmpty(result.MigrationSteps);
    }

    [Fact]
    public async Task AssessSymbolRemoval_scores_order_service_dependencies()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"graphify-removal-{Guid.NewGuid():N}.db");
        var builder = new RoslynGraphBuilder();
        var snapshot = await builder.BuildAsync(SampleSolutionPath);

        await using var database = await GraphDatabase.OpenAsync(databasePath);
        await database.ReplaceSnapshotAsync(snapshot);

        var orderService = (await database.SearchNodesAsync("OrderService", limit: 1)).Single();
        var service = new ArchitectureAssessmentService();
        var result = await service.AssessSymbolRemovalAsync(database, orderService);

        Assert.Contains("Removal / change impact", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("injects", result.OutgoingLines.Single(), StringComparison.OrdinalIgnoreCase);
    }
}
