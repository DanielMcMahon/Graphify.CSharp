using System.Text;
using Graphify.CSharp.Core.Query;
using Graphify.CSharp.Core.Storage;
using Graphify.CSharp.Roslyn;

namespace Graphify.CSharp.Tests;

public class TableMigrationTraceServiceTests
{
    private static string SampleSolutionPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "SampleApp", "SampleApp.sln"));

    [Fact]
    public async Task TraceTable_finds_entity_sql_and_file_field()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"graphify-trace-{Guid.NewGuid():N}.db");
        var builder = new RoslynGraphBuilder();
        var snapshot = await builder.BuildAsync(SampleSolutionPath);
        await using var database = await GraphDatabase.OpenAsync(databasePath);
        await database.ReplaceSnapshotAsync(snapshot);

        var service = new TableMigrationTraceService();
        var result = await service.TraceAsync(database, "Documents");

        Assert.Contains("Documents", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("FilePath", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("DocumentRepository", result.Markdown, StringComparison.Ordinal);
        Assert.NotEmpty(result.FileFields);
    }
}
