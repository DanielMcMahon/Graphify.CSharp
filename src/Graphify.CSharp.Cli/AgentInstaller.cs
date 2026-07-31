using System.Text.Json;
using System.Text.Json.Nodes;
using Graphify.CSharp.Core;
using Graphify.CSharp.Core.Workspace;

namespace Graphify.CSharp.Cli;

internal sealed class AgentInstaller
{
    private const string SkillName = "graphify-csharp";
    private const string McpServerName = "graphify-csharp";

    public InstallResult Install(InstallOptions options)
    {
        var result = new InstallResult();
        var mcpDll = ResolveMcpDllPath();
        var cliDll = ResolveCliDllPath();
        var projectRoot = Path.GetFullPath(options.ProjectRoot ?? Directory.GetCurrentDirectory());
        var databasePath = Path.IsPathRooted(options.DatabasePath)
            ? options.DatabasePath
            : Path.GetFullPath(Path.Combine(projectRoot, options.DatabasePath));
        var solutionPath = string.IsNullOrWhiteSpace(options.SolutionPath)
            ? GraphWorkspace.DiscoverSolutionPath(projectRoot)
            : Path.GetFullPath(options.SolutionPath);

        if (options.InstallClaudeSkill)
        {
            var skillTarget = GetClaudeSkillDirectory();
            Directory.CreateDirectory(skillTarget);
            File.Copy(GetSkillSourcePath(), Path.Combine(skillTarget, "SKILL.md"), overwrite: true);
            result.Messages.Add($"Installed Claude Code skill: {Path.Combine(skillTarget, "SKILL.md")}");
        }

        if (options.InstallCursorSkill)
        {
            var skillTarget = GetSkillDirectory(options.ProjectScope, projectRoot);
            Directory.CreateDirectory(skillTarget);
            File.Copy(GetSkillSourcePath(), Path.Combine(skillTarget, "SKILL.md"), overwrite: true);
            result.Messages.Add($"Installed Cursor skill: {Path.Combine(skillTarget, "SKILL.md")}");
        }

        if (options.InstallCursorRule)
        {
            var ruleTarget = GetCursorRulePath(options.ProjectScope, projectRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(ruleTarget)!);
            File.Copy(GetCursorRuleSourcePath(), ruleTarget, overwrite: true);
            result.Messages.Add($"Installed Cursor rule: {ruleTarget}");
        }

        if (options.InstallCursorMcp)
        {
            var configPath = GetCursorMcpConfigPath(options.ProjectScope, projectRoot);
            MergeCursorMcpConfig(configPath, mcpDll, databasePath, solutionPath, projectRoot);
            result.Messages.Add($"Updated Cursor MCP config: {configPath}");
        }

        if (options.InstallCopilotMcp)
        {
            var configPath = GetCopilotMcpConfigPath();
            MergeCopilotMcpConfig(configPath, mcpDll, databasePath, solutionPath, projectRoot);
            result.Messages.Add($"Updated Copilot MCP config: {configPath}");
        }

        if (options.InstallOpenCodeMcp)
        {
            var configPath = GetOpenCodeConfigPath();
            MergeOpenCodeMcpConfig(configPath, mcpDll, databasePath, solutionPath, projectRoot);
            result.Messages.Add($"Updated OpenCode MCP config: {configPath}");
        }

        if (options.InstallGitHook)
        {
            GitHookInstaller.Install(projectRoot, cliDll);
            result.Messages.Add($"Installed git post-commit hook in {projectRoot}");
        }

        return result;
    }

    private static string ResolveMcpDllPath()
    {
        var cliDir = Path.GetDirectoryName(typeof(AgentInstaller).Assembly.Location)
            ?? AppContext.BaseDirectory;
        var sibling = Path.GetFullPath(Path.Combine(cliDir, "Graphify.CSharp.Mcp.dll"));
        if (File.Exists(sibling))
        {
            return sibling;
        }

        var devPath = Path.GetFullPath(Path.Combine(cliDir, "..", "Graphify.CSharp.Mcp", "bin", "Debug", "net9.0", "Graphify.CSharp.Mcp.dll"));
        if (File.Exists(devPath))
        {
            return devPath;
        }

        throw new FileNotFoundException("Could not locate Graphify.CSharp.Mcp.dll. Build the solution first: dotnet build");
    }

    private static string ResolveCliDllPath()
    {
        var cliDir = Path.GetDirectoryName(typeof(AgentInstaller).Assembly.Location)
            ?? AppContext.BaseDirectory;
        var sibling = Path.GetFullPath(Path.Combine(cliDir, "Graphify.CSharp.Cli.dll"));
        if (File.Exists(sibling))
        {
            return sibling;
        }

        return typeof(AgentInstaller).Assembly.Location;
    }

    private static string GetSkillSourcePath()
    {
        foreach (var start in new[]
                 {
                     Path.GetDirectoryName(typeof(AgentInstaller).Assembly.Location),
                     Directory.GetCurrentDirectory()
                 })
        {
            var dir = start;
            for (var depth = 0; depth < 8 && !string.IsNullOrEmpty(dir); depth++)
            {
                var candidate = Path.Combine(dir, "skills", SkillName, "SKILL.md");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }
        }

        throw new FileNotFoundException("Could not locate skills/graphify-csharp/SKILL.md");
    }

    private static string GetCursorRuleSourcePath()
    {
        foreach (var start in new[]
                 {
                     Path.GetDirectoryName(typeof(AgentInstaller).Assembly.Location),
                     Directory.GetCurrentDirectory()
                 })
        {
            var dir = start;
            for (var depth = 0; depth < 8 && !string.IsNullOrEmpty(dir); depth++)
            {
                var candidate = Path.Combine(dir, "assets", "cursor", "rules", "graphify-csharp.mdc");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }
        }

        throw new FileNotFoundException("Could not locate assets/cursor/rules/graphify-csharp.mdc");
    }

    private static string GetClaudeSkillDirectory() =>
        Path.Combine(GetHomeDirectory(), ".claude", "skills", SkillName);

    private static string GetSkillDirectory(bool projectScope, string projectRoot) =>
        projectScope
            ? Path.Combine(projectRoot, ".cursor", "skills", SkillName)
            : Path.Combine(GetHomeDirectory(), ".cursor", "skills", SkillName);

    private static string GetCursorRulePath(bool projectScope, string projectRoot) =>
        projectScope
            ? Path.Combine(projectRoot, ".cursor", "rules", "graphify-csharp.mdc")
            : Path.Combine(GetHomeDirectory(), ".cursor", "rules", "graphify-csharp.mdc");

    private static string GetCursorMcpConfigPath(bool projectScope, string projectRoot) =>
        projectScope
            ? Path.Combine(projectRoot, ".cursor", "mcp.json")
            : Path.Combine(GetHomeDirectory(), ".cursor", "mcp.json");

    private static string GetCopilotMcpConfigPath() =>
        Path.Combine(GetHomeDirectory(), ".copilot", "mcp-config.json");

    private static string GetOpenCodeConfigPath() =>
        Path.Combine(GetHomeDirectory(), ".config", "opencode", "opencode.json");

    private static string GetHomeDirectory() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static void MergeCursorMcpConfig(string configPath, string mcpDll, string databasePath, string? solutionPath, string projectRoot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        var root = LoadJsonObject(configPath);
        var servers = root["mcpServers"] as JsonObject ?? new JsonObject();
        servers[McpServerName] = CreateCursorMcpEntry(mcpDll, databasePath, solutionPath, projectRoot);
        root["mcpServers"] = servers;
        WriteJson(configPath, root);
    }

    private static void MergeCopilotMcpConfig(string configPath, string mcpDll, string databasePath, string? solutionPath, string projectRoot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        var root = LoadJsonObject(configPath);
        var servers = root["mcpServers"] as JsonObject ?? new JsonObject();
        servers[McpServerName] = CreateCopilotMcpEntry(mcpDll, databasePath, solutionPath, projectRoot);
        root["mcpServers"] = servers;
        WriteJson(configPath, root);
    }

    private static void MergeOpenCodeMcpConfig(string configPath, string mcpDll, string databasePath, string? solutionPath, string projectRoot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        var root = LoadJsonObject(configPath);
        var mcp = root["mcp"] as JsonObject ?? new JsonObject();
        mcp[SkillName] = CreateOpenCodeMcpEntry(mcpDll, databasePath, solutionPath, projectRoot);
        root["mcp"] = mcp;
        WriteJson(configPath, root);
    }

    private static JsonObject CreateEnvironment(string databasePath, string? solutionPath, string projectRoot)
    {
        var env = new JsonObject
        {
            [GraphifyEnvironment.DatabasePathVariable] = databasePath,
            [GraphifyEnvironment.ProjectRootVariable] = projectRoot
        };

        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            env[GraphifyEnvironment.SolutionPathVariable] = solutionPath;
        }

        return env;
    }

    private static JsonObject CreateCursorMcpEntry(string mcpDll, string databasePath, string? solutionPath, string projectRoot) => new()
    {
        ["command"] = "dotnet",
        ["args"] = new JsonArray(mcpDll),
        ["env"] = CreateEnvironment(databasePath, solutionPath, projectRoot)
    };

    private static JsonObject CreateCopilotMcpEntry(string mcpDll, string databasePath, string? solutionPath, string projectRoot) => new()
    {
        ["type"] = "stdio",
        ["command"] = "dotnet",
        ["args"] = new JsonArray(mcpDll),
        ["env"] = CreateEnvironment(databasePath, solutionPath, projectRoot)
    };

    private static JsonObject CreateOpenCodeMcpEntry(string mcpDll, string databasePath, string? solutionPath, string projectRoot) => new()
    {
        ["type"] = "local",
        ["command"] = new JsonArray("dotnet", mcpDll),
        ["enabled"] = true,
        ["environment"] = CreateEnvironment(databasePath, solutionPath, projectRoot)
    };

    private static JsonObject LoadJsonObject(string path)
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        var text = File.ReadAllText(path);
        return JsonNode.Parse(text) as JsonObject ?? new JsonObject();
    }

    private static void WriteJson(string path, JsonObject root)
    {
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json + Environment.NewLine);
    }
}

internal sealed class InstallOptions
{
    public bool ProjectScope { get; init; }
    public string ProjectRoot { get; init; } = Directory.GetCurrentDirectory();
    public string DatabasePath { get; init; } = ".graphify/graph.db";
    public string? SolutionPath { get; init; }
    public bool InstallClaudeSkill { get; init; }
    public bool InstallCursorSkill { get; init; }
    public bool InstallCursorRule { get; init; }
    public bool InstallCursorMcp { get; init; }
    public bool InstallCopilotMcp { get; init; }
    public bool InstallOpenCodeMcp { get; init; }
    public bool InstallGitHook { get; init; }
}

internal sealed class InstallResult
{
    public List<string> Messages { get; } = [];
}
