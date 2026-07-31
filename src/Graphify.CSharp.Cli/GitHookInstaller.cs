namespace Graphify.CSharp.Cli;

internal static class GitHookInstaller
{
    private const string HookMarker = "# graphify-csharp";

    public static void Install(string projectRoot, string cliDllPath)
    {
        var hooksDirectory = Path.Combine(projectRoot, ".git", "hooks");
        if (!Directory.Exists(hooksDirectory))
        {
            return;
        }

        var hookPath = Path.Combine(hooksDirectory, "post-commit");
        var hookBody = $"""
            #!/bin/sh
            {HookMarker}
            dotnet "{cliDllPath}" ensure-graph --project-root "{projectRoot}" >/dev/null 2>&1 &
            """;

        if (File.Exists(hookPath))
        {
            var existing = File.ReadAllText(hookPath);
            if (existing.Contains(HookMarker, StringComparison.Ordinal))
            {
                return;
            }

            File.AppendAllText(hookPath, Environment.NewLine + hookBody + Environment.NewLine);
        }
        else
        {
            File.WriteAllText(hookPath, hookBody + Environment.NewLine);
        }

        try
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(hookPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        catch
        {
            // Best effort only.
        }
    }
}
