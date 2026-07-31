using Graphify.CSharp.Core.Query;
using Graphify.CSharp.Core.Storage;
using Graphify.CSharp.Roslyn;

namespace Graphify.CSharp.Tests;

public class InvestigationServiceTests
{
    private static string SampleSolutionPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "SampleApp", "SampleApp.sln"));

    [Fact]
    public async Task Investigate_returns_explanation_and_files_for_known_symbol()
    {
        var temp = Path.Combine(Path.GetTempPath(), "graphify-investigate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            var databasePath = Path.Combine(temp, ".graphify", "graph.db");
            var builder = new RoslynGraphBuilder();
            var snapshot = await builder.BuildAsync(SampleSolutionPath);
            await using var database = await GraphDatabase.OpenAsync(databasePath);
            await database.ReplaceSnapshotAsync(snapshot);

            var service = new InvestigationService();
            var result = await service.InvestigateAsync(database, "how does CreateOrder work?", temp);

            Assert.Contains("OrderService", result.Markdown, StringComparison.Ordinal);
            Assert.NotEmpty(result.FilesToRead);
            Assert.NotEmpty(result.SuggestedFollowUps);
            Assert.True(File.Exists(result.HandoffPath));
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }
}
