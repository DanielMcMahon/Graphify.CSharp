using Graphify.CSharp.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Graphify.CSharp.Roslyn;

internal static class FileStorageGraphExtractor
{
    private static readonly string[] FileApiPrefixes =
    [
        "System.IO.File.",
        "System.IO.Directory."
    ];

    private static readonly string[] FileInterfaceHints =
    [
        "IFileStorage",
        "IBlobStorage",
        "IFileService",
        "IStorageService",
        "IFileProvider"
    ];

    public static void Extract(
        Project project,
        Compilation compilation,
        string assemblyName,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        foreach (var document in project.Documents)
        {
            if (!document.SupportsSyntaxTree)
            {
                continue;
            }

            var tree = document.GetSyntaxTreeAsync().GetAwaiter().GetResult();
            var semanticModel = document.GetSemanticModelAsync().GetAwaiter().GetResult();
            if (tree is null || semanticModel is null)
            {
                continue;
            }

            foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (symbol is null)
                {
                    continue;
                }

                var display = symbol.ContainingType.ToDisplayString() + "." + symbol.Name;
                if (!FileApiPrefixes.Any(prefix => display.StartsWith(prefix, StringComparison.Ordinal))
                    && !display.Contains("Blob", StringComparison.OrdinalIgnoreCase)
                    && !display.Contains("Upload", StringComparison.OrdinalIgnoreCase)
                    && !display.Contains("Download", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var enclosing = RoslynGraphBuilderHelpers.GetEnclosingCallableSymbol(semanticModel, invocation);
                if (enclosing is null)
                {
                    continue;
                }

                var storageId = $"storage:file-api|{display}";
                RoslynGraphBuilderHelpers.AddGraphNode(nodes, new GraphNode(
                    storageId,
                    NodeKind.Type,
                    symbol.Name,
                    display,
                    assemblyName,
                    document.FilePath,
                    invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    null,
                    """{"role":"file_storage_api"}"""));

                var callerId = RoslynGraphBuilderHelpers.GetSymbolId(enclosing, assemblyName);
                RoslynGraphBuilderHelpers.AddEdge(edges, new GraphEdge(
                    callerId,
                    storageId,
                    GraphRelation.UsesFileStorage,
                    GraphConfidence.Extracted,
                    document.FilePath,
                    invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
            }

            foreach (var typeDeclaration in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var symbol = semanticModel.GetDeclaredSymbol(typeDeclaration) as INamedTypeSymbol;
                if (symbol is null)
                {
                    continue;
                }

                foreach (var iface in symbol.AllInterfaces)
                {
                    if (!FileInterfaceHints.Any(hint => iface.Name.Contains(hint, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var storageId = $"storage:interface|{iface.ToDisplayString()}";
                    RoslynGraphBuilderHelpers.AddGraphNode(nodes, new GraphNode(
                        storageId,
                        NodeKind.Type,
                        iface.Name,
                        iface.ToDisplayString(),
                        assemblyName,
                        document.FilePath,
                        typeDeclaration.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        null,
                        """{"role":"file_storage_interface"}"""));

                    var typeId = RoslynGraphBuilderHelpers.GetSymbolId(symbol, assemblyName);
                    RoslynGraphBuilderHelpers.AddEdge(edges, new GraphEdge(
                        typeId,
                        storageId,
                        GraphRelation.UsesFileStorage,
                        GraphConfidence.Inferred,
                        document.FilePath,
                        typeDeclaration.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
                }
            }
        }
    }
}
