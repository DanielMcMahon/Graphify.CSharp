using Graphify.CSharp.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Graphify.CSharp.Roslyn;

internal static class DiRegistrationGraphExtractor
{
    private static readonly HashSet<string> RegistrationMethodNames = new(StringComparer.Ordinal)
    {
        "AddSingleton",
        "AddScoped",
        "AddTransient",
        "AddKeyedSingleton",
        "AddKeyedScoped",
        "AddKeyedTransient",
        "TryAddSingleton",
        "TryAddScoped",
        "TryAddTransient",
        "AddHttpClient",
        "AddHostedService",
        "AddDbContext"
    };

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

            ExtractRegistrations(tree.GetRoot(), semanticModel, assemblyName, document.FilePath, nodes, edges);
        }
    }

    private static void ExtractRegistrations(
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

            if (!RegistrationMethodNames.Contains(memberAccess.Name.Identifier.Text))
            {
                continue;
            }

            var caller = RoslynGraphBuilderHelpers.GetEnclosingCallableSymbol(semanticModel, invocation);
            if (caller is null)
            {
                continue;
            }

            if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol registrationMethod)
            {
                continue;
            }

            RoslynGraphBuilderHelpers.AddSymbolNode(nodes, caller, assemblyName, filePath, null);
            var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var lifetime = memberAccess.Name.Identifier.Text;

            foreach (var registeredType in GetRegisteredTypes(registrationMethod))
            {
                if (registeredType.TypeKind is not (TypeKind.Class or TypeKind.Interface or TypeKind.Struct))
                {
                    continue;
                }

                RoslynGraphBuilderHelpers.AddSymbolNode(nodes, registeredType, assemblyName, filePath, null);
                RoslynGraphBuilderHelpers.AddEdge(edges, new GraphEdge(
                    RoslynGraphBuilderHelpers.GetSymbolId(caller, assemblyName),
                    RoslynGraphBuilderHelpers.GetSymbolId(registeredType, assemblyName),
                    GraphRelation.Registers,
                    GraphConfidence.Extracted,
                    filePath,
                    line,
                    $$"""{"lifetime":"{{lifetime}}"}"""));
            }
        }
    }

    private static IEnumerable<ITypeSymbol> GetRegisteredTypes(IMethodSymbol registrationMethod)
    {
        if (registrationMethod.TypeArguments.Length == 1)
        {
            yield return registrationMethod.TypeArguments[0];
            yield break;
        }

        if (registrationMethod.TypeArguments.Length >= 2)
        {
            yield return registrationMethod.TypeArguments[0];
            yield return registrationMethod.TypeArguments[1];
        }
    }
}
