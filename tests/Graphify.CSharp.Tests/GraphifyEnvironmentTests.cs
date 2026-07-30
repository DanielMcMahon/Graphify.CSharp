using Graphify.CSharp.Core;

namespace Graphify.CSharp.Tests;

public sealed class GraphifyEnvironmentTests
{
    [Fact]
    public void ResolveDatabasePath_uses_explicit_path_when_provided()
    {
        Assert.Equal("/tmp/custom.db", GraphifyEnvironment.ResolveDatabasePath("/tmp/custom.db"));
    }

    [Fact]
    public void ResolveDatabasePath_uses_environment_variable_when_path_not_provided()
    {
        var previous = Environment.GetEnvironmentVariable(GraphifyEnvironment.DatabasePathVariable);
        try
        {
            Environment.SetEnvironmentVariable(GraphifyEnvironment.DatabasePathVariable, "/tmp/from-env.db");
            Assert.Equal("/tmp/from-env.db", GraphifyEnvironment.ResolveDatabasePath());
        }
        finally
        {
            Environment.SetEnvironmentVariable(GraphifyEnvironment.DatabasePathVariable, previous);
        }
    }

    [Fact]
    public void ResolveDatabasePath_falls_back_to_default()
    {
        var previous = Environment.GetEnvironmentVariable(GraphifyEnvironment.DatabasePathVariable);
        try
        {
            Environment.SetEnvironmentVariable(GraphifyEnvironment.DatabasePathVariable, null);
            Assert.Equal(GraphifyEnvironment.DefaultDatabasePath, GraphifyEnvironment.ResolveDatabasePath());
        }
        finally
        {
            Environment.SetEnvironmentVariable(GraphifyEnvironment.DatabasePathVariable, previous);
        }
    }
}
