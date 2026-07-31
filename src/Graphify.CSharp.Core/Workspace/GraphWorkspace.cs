using System.Text.Json;
using Graphify.CSharp.Core.Storage;

namespace Graphify.CSharp.Core.Workspace;

public sealed record GraphWorkspaceConfig(
    string SolutionPath,
    string DatabasePath,
    string? JsonPath,
    DateTimeOffset ConfiguredAt);

public sealed record GraphWorkspaceStatus(
    bool ConfigExists,
    bool DatabaseExists,
    bool IsStale,
    string? SolutionPath,
    string DatabasePath,
    DateTimeOffset? BuiltAt,
    int NodeCount,
    int EdgeCount,
    string? NewestSourceChange);

public static class GraphWorkspace
{
    public const string ConfigDirectory = ".graphify";
    public const string ConfigFileName = "config.json";
    public const string ReportFileName = "GRAPH_REPORT.md";

    private static readonly string[] SourceExtensions = [".cs", ".csproj", ".sln"];

    public static string GetConfigPath(string projectRoot) =>
        Path.Combine(projectRoot, ConfigDirectory, ConfigFileName);

    public static string GetDefaultDatabasePath(string projectRoot) =>
        Path.Combine(projectRoot, ConfigDirectory, "graph.db");

    public static string GetDefaultJsonPath(string projectRoot) =>
        Path.Combine(projectRoot, ConfigDirectory, "graph.json");

    public static string GetReportPath(string projectRoot) =>
        Path.Combine(projectRoot, ConfigDirectory, ReportFileName);

    public static GraphWorkspaceConfig CreateConfig(string projectRoot, string solutionPath, string? databasePath = null, string? jsonPath = null)
    {
        var config = new GraphWorkspaceConfig(
            Path.GetFullPath(solutionPath),
            databasePath ?? GetDefaultDatabasePath(projectRoot),
            jsonPath ?? GetDefaultJsonPath(projectRoot),
            DateTimeOffset.UtcNow);

        SaveConfig(projectRoot, config);
        return config;
    }

    public static GraphWorkspaceConfig? LoadConfig(string projectRoot)
    {
        var configPath = GetConfigPath(projectRoot);
        if (!File.Exists(configPath))
        {
            return null;
        }

        var json = File.ReadAllText(configPath);
        var document = JsonSerializer.Deserialize<GraphWorkspaceConfigDto>(json);
        if (document is null || string.IsNullOrWhiteSpace(document.SolutionPath))
        {
            return null;
        }

        return new GraphWorkspaceConfig(
            document.SolutionPath,
            document.DatabasePath ?? GetDefaultDatabasePath(projectRoot),
            document.JsonPath,
            document.ConfiguredAt);
    }

    public static void SaveConfig(string projectRoot, GraphWorkspaceConfig config)
    {
        var configPath = GetConfigPath(projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        var dto = new GraphWorkspaceConfigDto
        {
            SolutionPath = config.SolutionPath,
            DatabasePath = config.DatabasePath,
            JsonPath = config.JsonPath,
            ConfiguredAt = config.ConfiguredAt
        };

        File.WriteAllText(configPath, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    public static string? DiscoverProjectRoot(string? startDirectory = null)
    {
        var directory = Path.GetFullPath(startDirectory ?? Directory.GetCurrentDirectory());
        for (var depth = 0; depth < 12; depth++)
        {
            if (File.Exists(GetConfigPath(directory)) || Directory.Exists(Path.Combine(directory, ConfigDirectory)))
            {
                return directory;
            }

            if (Directory.EnumerateFiles(directory, "*.sln", SearchOption.TopDirectoryOnly).Any())
            {
                return directory;
            }

            var parent = Directory.GetParent(directory)?.FullName;
            if (string.IsNullOrEmpty(parent))
            {
                break;
            }

            directory = parent;
        }

        return null;
    }

    public static string? DiscoverSolutionPath(string projectRoot)
    {
        var config = LoadConfig(projectRoot);
        if (config is not null && File.Exists(config.SolutionPath))
        {
            return config.SolutionPath;
        }

        return Directory
            .EnumerateFiles(projectRoot, "*.sln", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static async Task<GraphWorkspaceStatus> GetStatusAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        var config = LoadConfig(projectRoot);
        var databasePath = config?.DatabasePath ?? GetDefaultDatabasePath(projectRoot);
        var solutionPath = config?.SolutionPath ?? DiscoverSolutionPath(projectRoot);
        var databaseExists = File.Exists(databasePath);
        DateTimeOffset? builtAt = null;
        var nodeCount = 0;
        var edgeCount = 0;

        if (databaseExists)
        {
            await using var database = await GraphDatabase.OpenAsync(databasePath, cancellationToken).ConfigureAwait(false);
            var metadata = await database.GetMetadataAsync(cancellationToken).ConfigureAwait(false);
            if (metadata.TryGetValue("built_at", out var builtAtText) && DateTimeOffset.TryParse(builtAtText, out var parsedBuiltAt))
            {
                builtAt = parsedBuiltAt;
            }

            if (metadata.TryGetValue("node_count", out var nodeCountText) && int.TryParse(nodeCountText, out var parsedNodeCount))
            {
                nodeCount = parsedNodeCount;
            }

            if (metadata.TryGetValue("edge_count", out var edgeCountText) && int.TryParse(edgeCountText, out var parsedEdgeCount))
            {
                edgeCount = parsedEdgeCount;
            }
        }

        var newestSourceChange = GetNewestSourceChange(projectRoot, solutionPath);
        var isStale = databaseExists && builtAt.HasValue && newestSourceChange.HasValue && newestSourceChange > builtAt;

        return new GraphWorkspaceStatus(
            config is not null,
            databaseExists,
            isStale,
            solutionPath,
            databasePath,
            builtAt,
            nodeCount,
            edgeCount,
            newestSourceChange?.ToString("O"));
    }

    public static DateTimeOffset? GetNewestSourceChange(string projectRoot, string? solutionPath)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { projectRoot };
        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            var solutionDirectory = Path.GetDirectoryName(solutionPath);
            if (!string.IsNullOrWhiteSpace(solutionDirectory))
            {
                roots.Add(solutionDirectory);
            }
        }

        DateTimeOffset? newest = null;
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var extension in SourceExtensions)
            {
                foreach (var file in Directory.EnumerateFiles(root, $"*{extension}", SearchOption.AllDirectories))
                {
                    if (ShouldIgnorePath(file))
                    {
                        continue;
                    }

                    var lastWrite = File.GetLastWriteTimeUtc(file);
                    var candidate = new DateTimeOffset(lastWrite, TimeSpan.Zero);
                    if (newest is null || candidate > newest)
                    {
                        newest = candidate;
                    }
                }
            }
        }

        return newest;
    }

    private static bool ShouldIgnorePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/.graphify/", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class GraphWorkspaceConfigDto
    {
        public string SolutionPath { get; set; } = string.Empty;
        public string? DatabasePath { get; set; }
        public string? JsonPath { get; set; }
        public DateTimeOffset ConfiguredAt { get; set; }
    }
}
