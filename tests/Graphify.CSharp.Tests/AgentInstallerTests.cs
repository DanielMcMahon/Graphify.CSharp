using System.Text.Json.Nodes;
using Graphify.CSharp.Cli;

namespace Graphify.CSharp.Tests;

public class AgentInstallerTests
{
    [Fact]
    public void Install_writes_cursor_skill_and_mcp_config()
    {
        var temp = Path.Combine(Path.GetTempPath(), "graphify-install-" + Guid.NewGuid().ToString("N"));
        var home = Path.Combine(temp, "home");
        var cwd = Path.Combine(temp, "project");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(cwd);

        var previousHome = Environment.GetEnvironmentVariable("HOME");
        var previousUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        var previousCwd = Directory.GetCurrentDirectory();

        try
        {
            Environment.SetEnvironmentVariable("HOME", home);
            Environment.SetEnvironmentVariable("USERPROFILE", home);
            Directory.SetCurrentDirectory(cwd);

            var installer = new AgentInstaller();
            var result = installer.Install(new InstallOptions
            {
                ProjectScope = true,
                ProjectRoot = cwd,
                DatabasePath = ".graphify/graph.db",
                InstallCursorSkill = true,
                InstallCursorRule = true,
                InstallCursorMcp = true
            });

            Assert.Equal(3, result.Messages.Count);
            Assert.True(File.Exists(Path.Combine(cwd, ".cursor", "skills", "graphify-csharp", "SKILL.md")));
            Assert.True(File.Exists(Path.Combine(cwd, ".cursor", "rules", "graphify-csharp.mdc")));

            var mcpPath = Path.Combine(cwd, ".cursor", "mcp.json");
            Assert.True(File.Exists(mcpPath));
            var mcp = JsonNode.Parse(File.ReadAllText(mcpPath))!.AsObject();
            Assert.NotNull(mcp["mcpServers"]?["graphify-csharp"]);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, ".graphify/graph.db")), mcp["mcpServers"]!["graphify-csharp"]!["env"]!["GRAPHIFY_DB"]!.GetValue<string>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", previousHome);
            Environment.SetEnvironmentVariable("USERPROFILE", previousUserProfile);
            Directory.SetCurrentDirectory(previousCwd);
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }
}
