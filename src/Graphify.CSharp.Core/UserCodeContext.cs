using System.Text.Json;
using Graphify.CSharp.Core.Models;

namespace Graphify.CSharp.Core;

public sealed class UserCodeContext
{
    private static readonly string[] FrameworkAssemblyPrefixes =
    [
        "System",
        "Microsoft",
        "netstandard",
        "mscorlib",
        "NuGet.",
        "FluentAssertions",
        "xunit",
        "nunit",
        "Moq",
        "Castle.",
        "AutoFixture",
        "Bogus",
        "Serilog",
        "Swashbuckle",
        "Newtonsoft.",
        "Grpc.",
        "Google.",
        "OpenTelemetry",
        "Polly",
        "StackExchange.",
        "IdentityModel",
        "Humanizer",
        "MediatR",
        "AutoMapper",
        "Dapper",
        "Npgsql",
        "Pomelo.",
        "EFCore",
        "Aspire.",
        "OpenIddict"
    ];

    public HashSet<string> UserAssemblies { get; }

    private UserCodeContext(HashSet<string> userAssemblies) => UserAssemblies = userAssemblies;

    public static UserCodeContext FromMetadata(IReadOnlyDictionary<string, string> metadata) =>
        metadata.TryGetValue("user_assemblies", out var json) && !string.IsNullOrWhiteSpace(json)
            ? new UserCodeContext(JsonSerializer.Deserialize<HashSet<string>>(json) ?? new HashSet<string>(StringComparer.Ordinal))
            : new UserCodeContext(new HashSet<string>(StringComparer.Ordinal));

    public static UserCodeContext FromAssemblies(IEnumerable<string> assemblies) =>
        new(assemblies.ToHashSet(StringComparer.Ordinal));

    public static bool IsTestAssembly(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return false;
        }

        return assemblyName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            || assemblyName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)
            || assemblyName.Contains(".Test.", StringComparison.OrdinalIgnoreCase)
            || assemblyName.Contains(".IntegrationTests", StringComparison.OrdinalIgnoreCase)
            || assemblyName.Contains(".E2E", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFrameworkAssembly(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return false;
        }

        return FrameworkAssemblyPrefixes.Any(prefix =>
            assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsUserNode(GraphNode node)
    {
        if (string.IsNullOrWhiteSpace(node.Assembly))
        {
            return false;
        }

        if (IsTestAssembly(node.Assembly) || IsFrameworkAssembly(node.Assembly))
        {
            return false;
        }

        if (UserAssemblies.Count > 0)
        {
            return UserAssemblies.Contains(node.Assembly);
        }

        return node.FilePath is not null;
    }
}
