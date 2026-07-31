using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Query;
using Graphify.CSharp.Core.Storage;
using Graphify.CSharp.Roslyn;

namespace Graphify.CSharp.Tests;

public class StartupCompositionServiceTests
{
    private static string SampleSolutionPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "SampleApp", "SampleApp.sln"));

    [Fact]
    public async Task Build_extracts_program_registrations_and_startup_map()
    {
        var solutionPath = SampleSolutionPath;
        var databasePath = Path.Combine(Path.GetTempPath(), $"graphify-startup-{Guid.NewGuid():N}.db");

        var builder = new RoslynGraphBuilder();
        var snapshot = await builder.BuildAsync(solutionPath);

        await using var database = await GraphDatabase.OpenAsync(databasePath);
        await database.ReplaceSnapshotAsync(snapshot);

        var entryPoints = await database.FindStartupEntryPointsAsync();
        Assert.Contains(entryPoints, node => node.FilePath?.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase) == true);

        var service = new StartupCompositionService();
        var result = await service.GetStartupMapAsync(database);
        Assert.NotEmpty(result.EntryPoints);
        Assert.Contains(result.Graph.Edges, edge => edge.Relation == GraphRelation.Registers);
        Assert.Contains(result.Graph.Edges, edge => edge.Relation == GraphRelation.Injects);
        Assert.Contains(result.Graph.Nodes, node => node.Name == "OrderService");
    }
}
