using Graphify.CSharp.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Graphify.CSharp.Roslyn;

internal static class MediatorGraphExtractor
{
    private static readonly string[] SenderTypeNames = ["ISender", "IMediator"];

    public static void Extract(
        Project project,
        Compilation compilation,
        string assemblyName,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        ExtractHandlerEdges(compilation, assemblyName, nodes, edges);

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

            ExtractDispatchEdges(tree.GetRoot(), semanticModel, assemblyName, document.FilePath, nodes, edges);
            ExtractPublishEdges(tree.GetRoot(), semanticModel, assemblyName, document.FilePath, nodes, edges);
        }
    }

    private static void ExtractHandlerEdges(
        Compilation compilation,
        string assemblyName,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        foreach (var typeSymbol in GetAllTypes(compilation.Assembly.GlobalNamespace))
        {
            foreach (var iface in typeSymbol.Interfaces)
            {
                if (iface.Name is not ("IRequestHandler" or "INotificationHandler"))
                {
                    continue;
                }

                if (iface.TypeArguments.Length < 1)
                {
                    continue;
                }

                var requestType = iface.TypeArguments[0];
                if (requestType.TypeKind == TypeKind.Error)
                {
                    continue;
                }

                RoslynGraphBuilderHelpers.AddSymbolNode(nodes, typeSymbol, assemblyName, null, null);
                RoslynGraphBuilderHelpers.AddSymbolNode(nodes, requestType, assemblyName, null, null);

                RoslynGraphBuilderHelpers.AddEdge(edges, new GraphEdge(
                    RoslynGraphBuilderHelpers.GetSymbolId(typeSymbol, assemblyName),
                    RoslynGraphBuilderHelpers.GetSymbolId(requestType, assemblyName),
                    GraphRelation.Handles,
                    GraphConfidence.Extracted,
                    null,
                    null,
                    """{"source":"mediator_handler_registration"}"""));
            }
        }
    }

    private static void ExtractDispatchEdges(
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

            if (memberAccess.Name.Identifier.Text is not ("Send" or "SendAsync"))
            {
                continue;
            }

            var receiverSymbol = semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
            if (receiverSymbol is not IParameterSymbol parameter || !IsSenderParameter(parameter))
            {
                continue;
            }

            var caller = RoslynGraphBuilderHelpers.GetEnclosingCallableSymbol(semanticModel, invocation);
            if (caller is null)
            {
                continue;
            }

            var requestType = TryGetRequestType(invocation, semanticModel);
            if (requestType is null || requestType.TypeKind == TypeKind.Error)
            {
                continue;
            }

            RoslynGraphBuilderHelpers.AddSymbolNode(nodes, caller, assemblyName, filePath, null);
            RoslynGraphBuilderHelpers.AddSymbolNode(nodes, requestType, assemblyName, filePath, null);
            RoslynGraphBuilderHelpers.AddEdge(edges, new GraphEdge(
                RoslynGraphBuilderHelpers.GetSymbolId(caller, assemblyName),
                RoslynGraphBuilderHelpers.GetSymbolId(requestType, assemblyName),
                GraphRelation.Dispatches,
                GraphConfidence.Extracted,
                filePath,
                invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                """{"source":"mediator_send"}"""));
        }
    }

    private static void ExtractPublishEdges(
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

            if (memberAccess.Name.Identifier.Text is not ("Publish" or "PublishAsync"))
            {
                continue;
            }

            var receiverSymbol = semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
            if (receiverSymbol is not IParameterSymbol parameter || !IsSenderParameter(parameter))
            {
                continue;
            }

            var caller = RoslynGraphBuilderHelpers.GetEnclosingCallableSymbol(semanticModel, invocation);
            if (caller is null)
            {
                continue;
            }

            var notificationType = TryGetRequestType(invocation, semanticModel);
            if (notificationType is null || notificationType.TypeKind == TypeKind.Error)
            {
                continue;
            }

            RoslynGraphBuilderHelpers.AddSymbolNode(nodes, caller, assemblyName, filePath, null);
            RoslynGraphBuilderHelpers.AddSymbolNode(nodes, notificationType, assemblyName, filePath, null);
            RoslynGraphBuilderHelpers.AddEdge(edges, new GraphEdge(
                RoslynGraphBuilderHelpers.GetSymbolId(caller, assemblyName),
                RoslynGraphBuilderHelpers.GetSymbolId(notificationType, assemblyName),
                GraphRelation.Publishes,
                GraphConfidence.Extracted,
                filePath,
                invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                """{"source":"mediator_publish"}"""));
        }
    }

    private static bool IsSenderParameter(IParameterSymbol parameter)
    {
        var typeName = parameter.Type.Name;
        if (SenderTypeNames.Contains(typeName))
        {
            return true;
        }

        return parameter.Type.AllInterfaces.Any(iface => SenderTypeNames.Contains(iface.Name));
    }

    private static ITypeSymbol? TryGetRequestType(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
        {
            return null;
        }

        var argument = invocation.ArgumentList.Arguments[0].Expression;
        var typeInfo = semanticModel.GetTypeInfo(argument);
        if (typeInfo.Type is not null && typeInfo.Type.SpecialType == SpecialType.None)
        {
            return typeInfo.Type;
        }

        if (argument is ObjectCreationExpressionSyntax objectCreation)
        {
            return semanticModel.GetTypeInfo(objectCreation).Type;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(argument);
        return symbolInfo.Symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol param => param.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol childNamespace)
            {
                foreach (var nested in GetAllTypes(childNamespace))
                {
                    yield return nested;
                }
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;
                foreach (var nestedType in GetAllNestedTypes(type))
                {
                    yield return nestedType;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetAllNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var child in GetAllNestedTypes(nested))
            {
                yield return child;
            }
        }
    }
}
