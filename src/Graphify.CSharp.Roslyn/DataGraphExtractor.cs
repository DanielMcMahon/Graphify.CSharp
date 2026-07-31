using System.Text.RegularExpressions;
using Graphify.CSharp.Core;
using Graphify.CSharp.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Graphify.CSharp.Roslyn;

internal static class DataGraphExtractor
{
    private static readonly Regex SqlTableRegex = new(
        @"\b(?:FROM|JOIN|INTO|UPDATE|DELETE\s+FROM)\s+\[?(?<table>[A-Za-z_][A-Za-z0-9_]*)\]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

            var root = tree.GetRoot();
            ExtractEntityMappings(root, semanticModel, assemblyName, document.FilePath, nodes, edges);
            ExtractDbContextSets(root, semanticModel, assemblyName, document.FilePath, nodes, edges);
            ExtractFluentTableMappings(root, semanticModel, assemblyName, document.FilePath, nodes, edges);
            ExtractSqlLiterals(root, semanticModel, assemblyName, document.FilePath, nodes, edges);
        }
    }

    private static void ExtractEntityMappings(
        SyntaxNode root,
        SemanticModel semanticModel,
        string assemblyName,
        string? filePath,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        foreach (var typeDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            var symbol = semanticModel.GetDeclaredSymbol(typeDeclaration) as INamedTypeSymbol;
            if (symbol is null || symbol.TypeKind != TypeKind.Class)
            {
                continue;
            }

            var tableName = GetTableAttributeName(symbol) ?? symbol.Name;
            LinkEntityToTable(symbol, tableName, assemblyName, filePath, typeDeclaration.GetLocation().GetLineSpan().StartLinePosition.Line + 1, nodes, edges);
            ExtractFileFields(symbol, assemblyName, filePath, nodes, edges);
        }
    }

    private static void ExtractDbContextSets(
        SyntaxNode root,
        SemanticModel semanticModel,
        string assemblyName,
        string? filePath,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            var symbol = semanticModel.GetDeclaredSymbol(property) as IPropertySymbol;
            if (symbol is null)
            {
                continue;
            }

            if (symbol.Type is not INamedTypeSymbol { IsGenericType: true } namedType)
            {
                continue;
            }

            if (!string.Equals(namedType.Name, "DbSet", StringComparison.Ordinal) || namedType.TypeArguments.Length != 1)
            {
                continue;
            }

            var entityType = namedType.TypeArguments[0] as INamedTypeSymbol;
            if (entityType is null)
            {
                continue;
            }

            var tableName = GetTableAttributeName(entityType) ?? entityType.Name;
            LinkEntityToTable(entityType, tableName, assemblyName, filePath, property.GetLocation().GetLineSpan().StartLinePosition.Line + 1, nodes, edges);
            ExtractFileFields(entityType, assemblyName, filePath, nodes, edges);
        }
    }

    private static void ExtractFluentTableMappings(
        SyntaxNode root,
        SemanticModel semanticModel,
        string assemblyName,
        string? filePath,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                continue;
            }

            if (!string.Equals(memberAccess.Name.Identifier.Text, "ToTable", StringComparison.Ordinal))
            {
                continue;
            }

            if (invocation.ArgumentList.Arguments.Count == 0)
            {
                continue;
            }

            var tableName = RoslynGraphBuilderHelpers.TryGetConstantString(invocation.ArgumentList.Arguments[0].Expression);
            if (string.IsNullOrWhiteSpace(tableName))
            {
                continue;
            }

            var entityType = TryResolveEntityTypeFromFluentChain(memberAccess, semanticModel);
            if (entityType is null)
            {
                continue;
            }

            LinkEntityToTable(entityType, tableName, assemblyName, filePath, invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1, nodes, edges);
        }
    }

    private static void ExtractSqlLiterals(
        SyntaxNode root,
        SemanticModel semanticModel,
        string assemblyName,
        string? filePath,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        foreach (var literal in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression))
            {
                continue;
            }

            var text = literal.Token.ValueText;
            if (string.IsNullOrWhiteSpace(text) || text.Length < 8)
            {
                continue;
            }

            var enclosingSymbol = semanticModel.GetEnclosingSymbol(literal.SpanStart);
            if (enclosingSymbol is null)
            {
                continue;
            }

            foreach (Match match in SqlTableRegex.Matches(text))
            {
                var tableName = match.Groups["table"].Value;
                LinkQueryToTable(enclosingSymbol, tableName, assemblyName, filePath, literal.GetLocation().GetLineSpan().StartLinePosition.Line + 1, nodes, edges);
            }
        }
    }

    private static void LinkEntityToTable(
        INamedTypeSymbol entityType,
        string tableName,
        string assemblyName,
        string? filePath,
        int? line,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        var tableId = SymbolId.ForTable(tableName);
        RoslynGraphBuilderHelpers.AddGraphNode(nodes, new GraphNode(
            tableId,
            NodeKind.Table,
            tableName,
            tableName,
            assemblyName,
            filePath,
            line,
            null,
            """{"source":"entity_mapping"}"""));

        var entityId = SymbolId.ForSymbol(assemblyName, entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        RoslynGraphBuilderHelpers.AddEdge(edges, new GraphEdge(
            entityId,
            tableId,
            GraphRelation.MapsToTable,
            GraphConfidence.Extracted,
            filePath,
            line));
    }

    private static void LinkQueryToTable(
        ISymbol enclosingSymbol,
        string tableName,
        string assemblyName,
        string? filePath,
        int? line,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        var tableId = SymbolId.ForTable(tableName);
        RoslynGraphBuilderHelpers.AddGraphNode(nodes, new GraphNode(
            tableId,
            NodeKind.Table,
            tableName,
            tableName,
            assemblyName,
            filePath,
            line,
            null,
            """{"source":"sql_literal"}"""));

        var symbolAssembly = enclosingSymbol.ContainingAssembly.Name ?? assemblyName;
        var symbolId = SymbolId.ForSymbol(symbolAssembly, enclosingSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        RoslynGraphBuilderHelpers.AddEdge(edges, new GraphEdge(
            symbolId,
            tableId,
            GraphRelation.QueriesTable,
            GraphConfidence.Extracted,
            filePath,
            line));
    }

    private static void ExtractFileFields(
        INamedTypeSymbol entityType,
        string assemblyName,
        string? filePath,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        foreach (var member in entityType.GetMembers().OfType<IPropertySymbol>())
        {
            if (!LooksLikeFileField(member.Name))
            {
                continue;
            }

            var fieldId = $"{SymbolId.ForSymbol(assemblyName, entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))}#file:{member.Name}";
            RoslynGraphBuilderHelpers.AddGraphNode(nodes, new GraphNode(
                fieldId,
                NodeKind.FileField,
                member.Name,
                $"{entityType.Name}.{member.Name}",
                assemblyName,
                filePath,
                null,
                null,
                """{"role":"file_reference_column"}"""));

            var entityId = SymbolId.ForSymbol(assemblyName, entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            RoslynGraphBuilderHelpers.AddEdge(edges, new GraphEdge(
                entityId,
                fieldId,
                GraphRelation.HasFileField,
                GraphConfidence.Inferred,
                filePath,
                null));
        }
    }

    private static bool LooksLikeFileField(string name) =>
        name.Contains("File", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Path", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Blob", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Document", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Attachment", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Storage", StringComparison.OrdinalIgnoreCase);

    private static string? GetTableAttributeName(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var className = attribute.AttributeClass?.Name;
            if (className is not ("TableAttribute" or "Table"))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string tableName)
            {
                return tableName;
            }
        }

        return null;
    }

    private static INamedTypeSymbol? TryResolveEntityTypeFromFluentChain(MemberAccessExpressionSyntax memberAccess, SemanticModel semanticModel)
    {
        var expression = memberAccess.Expression;
        if (expression is InvocationExpressionSyntax invocation
            && invocation.Expression is MemberAccessExpressionSyntax entityAccess
            && string.Equals(entityAccess.Name.Identifier.Text, "Entity", StringComparison.Ordinal))
        {
            var typeInfo = semanticModel.GetTypeInfo(invocation);
            return typeInfo.Type as INamedTypeSymbol;
        }

        return semanticModel.GetTypeInfo(expression).Type as INamedTypeSymbol;
    }
}
