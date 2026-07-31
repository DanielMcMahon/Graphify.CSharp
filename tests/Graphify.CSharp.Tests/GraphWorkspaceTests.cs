using Graphify.CSharp.Core.Workspace;

namespace Graphify.CSharp.Tests;

public class GraphWorkspaceTests
{
    [Fact]
    public void DiscoverProjectRoot_finds_directory_with_solution()
    {
        var temp = CreateTempRepo(withSolution: true, withConfig: false);
        try
        {
            var discovered = GraphWorkspace.DiscoverProjectRoot(temp);
            Assert.Equal(temp, discovered);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void CreateConfig_and_LoadConfig_roundtrip()
    {
        var temp = CreateTempRepo(withSolution: true, withConfig: false);
        try
        {
            var solutionPath = Path.Combine(temp, "Sample.sln");
            var config = GraphWorkspace.CreateConfig(temp, solutionPath);
            var loaded = GraphWorkspace.LoadConfig(temp);

            Assert.NotNull(loaded);
            Assert.Equal(config.SolutionPath, loaded!.SolutionPath);
            Assert.Equal(config.DatabasePath, loaded.DatabasePath);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static string CreateTempRepo(bool withSolution, bool withConfig)
    {
        var temp = Path.Combine(Path.GetTempPath(), "graphify-workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        if (withSolution)
        {
            File.WriteAllText(Path.Combine(temp, "Sample.sln"), string.Empty);
        }

        if (withConfig)
        {
            GraphWorkspace.CreateConfig(temp, Path.Combine(temp, "Sample.sln"));
        }

        return temp;
    }
}
