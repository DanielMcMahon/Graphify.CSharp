using Graphify.CSharp.Core;
using Graphify.CSharp.Core.Export;
using Graphify.CSharp.Core.Query;
using Graphify.CSharp.Core.Storage;
using Graphify.CSharp.Core.Workspace;
using Graphify.CSharp.Roslyn;

namespace Graphify.CSharp.Cli;

internal sealed class AgentSetupService
{
    private readonly AgentInstaller _installer = new();
    private readonly GraphReportGenerator _reportGenerator = new();

    public async Task<SetupResult> InitializeAsync(SetupOptions options, CancellationToken cancellationToken = default)
    {
        var projectRoot = Path.GetFullPath(options.ProjectRoot ?? Directory.GetCurrentDirectory());
        var solutionPath = options.SolutionPath ?? GraphWorkspace.DiscoverSolutionPath(projectRoot);
        if (string.IsNullOrWhiteSpace(solutionPath) || !File.Exists(solutionPath))
        {
            throw new FileNotFoundException("Could not find a .sln file. Pass --solution explicitly.");
        }

        var databasePath = options.DatabasePath ?? GraphWorkspace.GetDefaultDatabasePath(projectRoot);
        var jsonPath = options.JsonPath ?? GraphWorkspace.GetDefaultJsonPath(projectRoot);
        Directory.CreateDirectory(Path.Combine(projectRoot, GraphWorkspace.ConfigDirectory));

        var config = GraphWorkspace.CreateConfig(projectRoot, solutionPath, databasePath, jsonPath);
        var buildResult = await BuildGraphAsync(solutionPath, databasePath, jsonPath, cancellationToken).ConfigureAwait(false);

        await using (var database = await GraphDatabase.OpenAsync(databasePath, cancellationToken).ConfigureAwait(false))
        {
            await _reportGenerator.WriteAsync(projectRoot, database, cancellationToken).ConfigureAwait(false);
        }

        var messages = new List<string>
        {
            $"Configured workspace at {projectRoot}",
            $"Solution: {solutionPath}",
            $"Database: {databasePath}",
            buildResult,
            $"Report: {GraphWorkspace.GetReportPath(projectRoot)}"
        };

        if (options.InstallAgents)
        {
            var installResult = _installer.Install(new InstallOptions
            {
                ProjectScope = true,
                DatabasePath = databasePath,
                SolutionPath = solutionPath,
                ProjectRoot = projectRoot,
                InstallClaudeSkill = options.GlobalAgents,
                InstallCursorSkill = true,
                InstallCursorMcp = true,
                InstallCursorRule = true,
                InstallCopilotMcp = options.GlobalAgents,
                InstallOpenCodeMcp = options.GlobalAgents,
                InstallGitHook = options.InstallGitHook
            });
            messages.AddRange(installResult.Messages);
        }

        return new SetupResult(messages);
    }

    public async Task<string> EnsureGraphAsync(string? projectRoot = null, CancellationToken cancellationToken = default)
    {
        projectRoot = Path.GetFullPath(projectRoot ?? GraphWorkspace.DiscoverProjectRoot() ?? Directory.GetCurrentDirectory());
        var config = GraphWorkspace.LoadConfig(projectRoot);
        var solutionPath = config?.SolutionPath ?? GraphWorkspace.DiscoverSolutionPath(projectRoot);
        if (string.IsNullOrWhiteSpace(solutionPath) || !File.Exists(solutionPath))
        {
            return "No solution found. Run `graphify-csharp init` in the project root first.";
        }

        var databasePath = config?.DatabasePath ?? GraphWorkspace.GetDefaultDatabasePath(projectRoot);
        var jsonPath = config?.JsonPath ?? GraphWorkspace.GetDefaultJsonPath(projectRoot);
        var status = await GraphWorkspace.GetStatusAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        if (status.DatabaseExists && !status.IsStale)
        {
            return $"Graph is up to date ({status.NodeCount:N0} nodes, {status.EdgeCount:N0} edges) at {databasePath}";
        }

        if (!status.ConfigExists)
        {
            GraphWorkspace.CreateConfig(projectRoot, solutionPath, databasePath, jsonPath);
        }

        var buildResult = await BuildGraphAsync(solutionPath, databasePath, jsonPath, cancellationToken).ConfigureAwait(false);
        await using (var database = await GraphDatabase.OpenAsync(databasePath, cancellationToken).ConfigureAwait(false))
        {
            await _reportGenerator.WriteAsync(projectRoot, database, cancellationToken).ConfigureAwait(false);
        }

        return status.DatabaseExists
            ? $"Rebuilt stale graph. {buildResult}"
            : $"Built graph. {buildResult}";
    }

    private static async Task<string> BuildGraphAsync(string solutionPath, string databasePath, string? jsonPath, CancellationToken cancellationToken)
    {
        var builder = new RoslynGraphBuilder();
        var snapshot = await builder.BuildAsync(solutionPath, cancellationToken).ConfigureAwait(false);
        await using var database = await GraphDatabase.OpenAsync(databasePath, cancellationToken).ConfigureAwait(false);
        await database.ReplaceSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(jsonPath))
        {
            await GraphJsonExporter.WriteAsync(snapshot, jsonPath, cancellationToken).ConfigureAwait(false);
        }

        return $"Built {snapshot.Nodes.Count:N0} nodes and {snapshot.Edges.Count:N0} edges.";
    }
}

internal sealed class SetupOptions
{
    public string? ProjectRoot { get; init; }
    public string? SolutionPath { get; init; }
    public string? DatabasePath { get; init; }
    public string? JsonPath { get; init; }
    public bool InstallAgents { get; init; } = true;
    public bool GlobalAgents { get; init; }
    public bool InstallGitHook { get; init; } = true;
}

internal sealed class SetupResult
{
    public SetupResult(IEnumerable<string> messages) => Messages = messages.ToList();
    public List<string> Messages { get; }
}
