using System.Text.RegularExpressions;
using Graphify.CSharp.Core;
using Graphify.CSharp.Core.Models;

namespace Graphify.CSharp.Roslyn;

internal static class LegacyWebGraphExtractor
{
    private static readonly Regex PageDirectiveRegex = new(
        @"<%@\s*Page\b[^%]*\bInherits\s*=\s*""(?<inherits>[^""]+)""[^%]*%>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CodeBehindRegex = new(
        @"\bCodeBehind\s*=\s*""(?<codebehind>[^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void Extract(string solutionOrProjectPath, Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
    {
        var root = Directory.Exists(solutionOrProjectPath)
            ? solutionOrProjectPath
            : Path.GetDirectoryName(solutionOrProjectPath);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }

        foreach (var pagePath in Directory.EnumerateFiles(root, "*.aspx", SearchOption.AllDirectories))
        {
            if (ShouldIgnore(pagePath))
            {
                continue;
            }

            ExtractPage(pagePath, root, nodes, edges);
        }
    }

    private static void ExtractPage(string pagePath, string root, Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
    {
        var text = File.ReadAllText(pagePath);
        var inheritsMatch = PageDirectiveRegex.Match(text);
        if (!inheritsMatch.Success)
        {
            return;
        }

        var inherits = inheritsMatch.Groups["inherits"].Value.Trim();
        var relativePath = Path.GetRelativePath(root, pagePath).Replace('\\', '/');
        var pageId = SymbolId.ForPage(relativePath);
        RoslynGraphBuilderHelpers.AddGraphNode(nodes, new GraphNode(
            pageId,
            NodeKind.Page,
            Path.GetFileName(pagePath),
            relativePath,
            null,
            pagePath,
            1,
            null,
            $$"""{"inherits":"{{inherits}}"}"""));

        var codeBehindMatch = CodeBehindRegex.Match(text);
        var codeBehind = codeBehindMatch.Success ? codeBehindMatch.Groups["codebehind"].Value : null;
        var typeName = inherits.Split(',')[0].Trim();
        var typeId = $"sym:legacy|{typeName}";
        RoslynGraphBuilderHelpers.AddGraphNode(nodes, new GraphNode(
            typeId,
            NodeKind.Type,
            typeName.Split('.').Last(),
            typeName,
            "legacy",
            codeBehind is null ? null : Path.Combine(Path.GetDirectoryName(pagePath) ?? root, codeBehind),
            1,
            null,
            """{"source":"aspx_codebehind"}"""));

        RoslynGraphBuilderHelpers.AddEdge(edges, new GraphEdge(
            pageId,
            typeId,
            GraphRelation.PageCodeBehind,
            GraphConfidence.Extracted,
            pagePath,
            1));
    }

    private static bool ShouldIgnore(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase);
    }
}
