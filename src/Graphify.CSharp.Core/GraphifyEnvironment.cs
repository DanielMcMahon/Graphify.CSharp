namespace Graphify.CSharp.Core;

public static class GraphifyEnvironment
{
    public const string DatabasePathVariable = "GRAPHIFY_DB";
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
}
