using Graphify.CSharp.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Graphify.CSharp.Roslyn;

internal static class AspNetGraphExtractor
{
    private static readonly string[] MapMethodNames =
    [
        "MapGet", "MapPost", "MapPut", "MapDelete", "MapPatch", "MapMethods"
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

            ExtractMinimalApiRoutes(tree.GetRoot(), semanticModel, assemblyName, document.FilePath, nodes, edges);
            ExtractControllerRoutes(tree.GetRoot(), semanticModel, assemblyName, document.FilePath, nodes, edges);
        }
    }

    private static void ExtractMinimalApiRoutes(
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

            if (!MapMethodNames.Contains(memberAccess.Name.Identifier.Text))
            {
                continue;
            }

            var httpMethod = memberAccess.Name.Identifier.Text switch
            {
                "MapGet" => "GET",
                "MapPost" => "POST",
                "MapPut" => "PUT",
                "MapDelete" => "DELETE",
                "MapPatch" => "PATCH",
                _ => "ANY"
            };

            if (invocation.ArgumentList.Arguments.Count < 2)
            {
                continue;
            }

            var route = TryGetRouteTemplate(invocation.ArgumentList.Arguments[0].Expression);
            if (route is null)
            {
                continue;
            }

            var handlerSymbol = TryGetHandlerSymbol(invocation.ArgumentList.Arguments[1].Expression, semanticModel);
            if (handlerSymbol is null)
            {
                continue;
            }

            AddRouteEdge(nodes, edges, assemblyName, filePath, invocation, httpMethod, route, handlerSymbol);
        }
    }

    private static void ExtractControllerRoutes(
        SyntaxNode root,
        SemanticModel semanticModel,
        string assemblyName,
        string? filePath,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        foreach (var classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol || !InheritsController(classSymbol))
            {
                continue;
            }

            var classRoute = GetRouteTemplate(classDeclaration.AttributeLists) ?? classSymbol.Name;

            foreach (var methodDeclaration in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
            {
                var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);
                if (methodSymbol is null)
                {
                    continue;
                }

                var httpMethod = GetHttpMethod(methodDeclaration.AttributeLists);
                if (httpMethod is null)
                {
                    continue;
                }

                var methodRoute = GetRouteTemplate(methodDeclaration.AttributeLists) ?? methodSymbol.Name;
                var route = CombineRoutes(classRoute, methodRoute);
                AddRouteEdge(nodes, edges, assemblyName, filePath, methodDeclaration, httpMethod, route, methodSymbol);
            }
        }
    }

    private static void AddRouteEdge(
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges,
        string assemblyName,
        string? filePath,
        SyntaxNode syntax,
        string httpMethod,
        string route,
        ISymbol handlerSymbol)
    {
        var endpointId = $"endpoint:{assemblyName}|{httpMethod}:{route}";
        var endpointNode = new GraphNode(
            endpointId,
            NodeKind.Type,
            $"{httpMethod} {route}",
            $"{httpMethod} {route}",
            assemblyName,
            filePath,
            syntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            null,
            """{"file_type":"endpoint"}""");

        RoslynGraphBuilderHelpers.AddGraphNode(nodes, endpointNode);
        RoslynGraphBuilderHelpers.AddSymbolNode(nodes, handlerSymbol, assemblyName, filePath, syntax);
        RoslynGraphBuilderHelpers.AddEdge(edges, new GraphEdge(
            endpointId,
            RoslynGraphBuilderHelpers.GetSymbolId(handlerSymbol, assemblyName),
            GraphRelation.Routes,
            GraphConfidence.Extracted,
            filePath,
            syntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            """{"source":"aspnet_route"}"""));
    }

    private static bool InheritsController(INamedTypeSymbol classSymbol)
    {
        for (var current = classSymbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name is "ControllerBase" or "Controller")
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryGetRouteTemplate(ExpressionSyntax expression) =>
        expression switch
        {
            LiteralExpressionSyntax literal when literal.Token.Value is string value => value,
            InterpolatedStringExpressionSyntax interpolated => "/" + string.Join("/", interpolated.Contents.OfType<InterpolatedStringTextSyntax>().Select(content => content.TextToken.ValueText)),
            _ => null
        };

    private static ISymbol? TryGetHandlerSymbol(ExpressionSyntax expression, SemanticModel semanticModel) =>
        expression switch
        {
            IdentifierNameSyntax or MemberAccessExpressionSyntax => semanticModel.GetSymbolInfo(expression).Symbol,
            LambdaExpressionSyntax lambda => RoslynGraphBuilderHelpers.GetEnclosingCallableSymbol(semanticModel, lambda) ??
                                             semanticModel.GetSymbolInfo(lambda).Symbol,
            _ => semanticModel.GetSymbolInfo(expression).Symbol
        };

    private static string? GetHttpMethod(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var name = attribute.Name.ToString();
                if (name.StartsWith("Http", StringComparison.Ordinal) && name.EndsWith("Attribute", StringComparison.Ordinal))
                {
                    return name["Http".Length..^"Attribute".Length].ToUpperInvariant();
                }

                if (name.StartsWith("Http", StringComparison.Ordinal))
                {
                    return name["Http".Length..].ToUpperInvariant();
                }
            }
        }

        return null;
    }

    private static string? GetRouteTemplate(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (!attribute.Name.ToString().StartsWith("Route", StringComparison.Ordinal))
                {
                    continue;
                }

                if (attribute.ArgumentList?.Arguments.Count > 0 &&
                    attribute.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal &&
                    literal.Token.Value is string route)
                {
                    return route;
                }
            }
        }

        return null;
    }

    private static string CombineRoutes(string classRoute, string methodRoute)
    {
        if (methodRoute.StartsWith('/'))
        {
            return methodRoute;
        }

        return $"{classRoute.TrimEnd('/')}/{methodRoute}".Replace("//", "/");
    }
}
