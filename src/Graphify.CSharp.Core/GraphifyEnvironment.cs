namespace Graphify.CSharp.Core;

public static class GraphifyEnvironment
{
    public const string DatabasePathVariable = "GRAPHIFY_DB";
    public const string SolutionPathVariable = "GRAPHIFY_SOLUTION";
    public const string ProjectRootVariable = "GRAPHIFY_PROJECT_ROOT";
    public const string DefaultDatabasePath = ".graphify/graph.db";

    public static string ResolveDatabasePath(string? databasePath = null)
    {
        if (!string.IsNullOrWhiteSpace(databasePath))
        {
            return databasePath;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(DatabasePathVariable);
        return string.IsNullOrWhiteSpace(fromEnvironment)
            ? DefaultDatabasePath
            : fromEnvironment;
    }

    public static string? ResolveSolutionPath(string? solutionPath = null)
    {
        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            return solutionPath;
        }

        return Environment.GetEnvironmentVariable(SolutionPathVariable);
    }

    public static string ResolveProjectRoot(string? projectRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            return projectRoot;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(ProjectRootVariable);
        return string.IsNullOrWhiteSpace(fromEnvironment)
            ? Directory.GetCurrentDirectory()
            : fromEnvironment;
    }
}
