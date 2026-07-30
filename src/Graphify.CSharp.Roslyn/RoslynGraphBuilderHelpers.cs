using Graphify.CSharp.Core;
using Graphify.CSharp.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Graphify.CSharp.Roslyn;

internal static class RoslynGraphBuilderHelpers
{
    public static void AddSymbolNode(
        Dictionary<string, GraphNode> nodes,
        ISymbol symbol,
        string assemblyName,
        string? filePath,
        SyntaxNode? declarationSyntax)
    {
        AddNode(nodes, CreateSymbolNode(symbol, assemblyName, filePath, declarationSyntax));
    }

    public static void AddEdge(List<GraphEdge> edges, GraphEdge edge) => edges.Add(edge);

    public static void AddGraphNode(Dictionary<string, GraphNode> nodes, GraphNode node) =>
        nodes[node.Id] = node;

    public static string GetSymbolId(ISymbol symbol, string? fallbackAssembly = null) =>
        SymbolId.ForSymbol(
            symbol.ContainingAssembly?.Name ?? fallbackAssembly ?? "unknown",
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

    public static ISymbol? GetEnclosingCallableSymbol(SemanticModel semanticModel, SyntaxNode node)
    {
        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is not null)
        {
            return semanticModel.GetDeclaredSymbol(method);
        }

        var accessor = node.Ancestors().OfType<AccessorDeclarationSyntax>().FirstOrDefault();
        if (accessor is not null)
        {
            return semanticModel.GetDeclaredSymbol(accessor);
        }

        var constructor = node.Ancestors().OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
        return constructor is not null ? semanticModel.GetDeclaredSymbol(constructor) : null;
    }

    private static GraphNode CreateSymbolNode(ISymbol symbol, string assemblyName, string? filePath, SyntaxNode? declarationSyntax)
    {
        var location = declarationSyntax?.GetLocation() ?? symbol.Locations.FirstOrDefault();
        var lineSpan = location?.GetLineSpan();
        var resolvedAssembly = symbol.ContainingAssembly?.Name ?? assemblyName;
        return new GraphNode(
            GetSymbolId(symbol, assemblyName),
            GetNodeKind(symbol),
            symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            resolvedAssembly,
            lineSpan?.Path ?? filePath,
            lineSpan?.StartLinePosition.Line + 1,
            lineSpan?.EndLinePosition.Line + 1);
    }

    private static NodeKind GetNodeKind(ISymbol symbol) =>
        symbol switch
        {
            INamedTypeSymbol => NodeKind.Type,
            IMethodSymbol => NodeKind.Method,
            IPropertySymbol => NodeKind.Property,
            IFieldSymbol => NodeKind.Field,
            IEventSymbol => NodeKind.Event,
            IParameterSymbol => NodeKind.Parameter,
            INamespaceSymbol => NodeKind.Namespace,
            _ => NodeKind.Type
        };

    private static void AddNode(Dictionary<string, GraphNode> nodes, GraphNode node) =>
        nodes[node.Id] = node;
}
