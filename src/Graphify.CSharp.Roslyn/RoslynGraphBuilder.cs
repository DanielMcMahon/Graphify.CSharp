using Graphify.CSharp.Core;
using Graphify.CSharp.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.FindSymbols;

namespace Graphify.CSharp.Roslyn;

public sealed class RoslynGraphBuilder
{
    private HashSet<string> _userAssemblies = new(StringComparer.Ordinal);

    public async Task<GraphSnapshot> BuildAsync(string solutionOrProjectPath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(solutionOrProjectPath);
        var nodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        var edges = new List<GraphEdge>();
        _userAssemblies = new HashSet<string>(StringComparer.Ordinal);

        if (fullPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            await BuildFromSolutionAsync(fullPath, nodes, edges, cancellationToken).ConfigureAwait(false);
        }
        else if (fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            await BuildFromProjectAsync(fullPath, nodes, edges, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new ArgumentException("Path must be a .sln or .csproj file.", nameof(solutionOrProjectPath));
        }

        ExtractLegacyWebAssets(fullPath, nodes, edges);

        return new GraphSnapshot
        {
            SolutionPath = fullPath,
            BuiltAt = DateTimeOffset.UtcNow,
            Nodes = nodes.Values.ToList(),
            Edges = edges,
            UserAssemblies = _userAssemblies.ToList()
        };
    }

    private void ExtractLegacyWebAssets(string solutionOrProjectPath, Dictionary<string, GraphNode> nodes, List<GraphEdge> edges) =>
        LegacyWebGraphExtractor.Extract(solutionOrProjectPath, nodes, edges);

    private async Task BuildFromSolutionAsync(
        string solutionPath,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges,
        CancellationToken cancellationToken)
    {
        var workspace = await OpenWorkspaceAsync(cancellationToken).ConfigureAwait(false);
        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        _userAssemblies = solution.Projects
            .Where(project => project.Language == LanguageNames.CSharp)
            .Where(project => !IsTestProject(project))
            .Select(project => project.AssemblyName ?? project.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal)!;

        foreach (var project in solution.Projects)
        {
            if (project.Language != LanguageNames.CSharp)
            {
                continue;
            }

            ExtractProject(project, nodes, edges);
        }
    }

    private async Task BuildFromProjectAsync(
        string projectPath,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges,
        CancellationToken cancellationToken)
    {
        var workspace = await OpenWorkspaceAsync(cancellationToken).ConfigureAwait(false);
        var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!IsTestProject(project))
        {
            _userAssemblies.Add(project.AssemblyName ?? project.Name);
        }

        ExtractProject(project, nodes, edges);
    }

    private static bool IsTestProject(Project project)
    {
        var name = project.Name;
        return name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)
            || name.Contains("IntegrationTests", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".E2E", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Playwright", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<MSBuildWorkspace> OpenWorkspaceAsync(CancellationToken cancellationToken)
    {
        var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, args) =>
        {
            // Non-fatal diagnostics are common for design-time builds; extraction continues with what compiled.
        };

        await Task.CompletedTask;
        return workspace;
    }

    private static void ExtractProject(Project project, Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
    {
        var compilation = project.GetCompilationAsync().GetAwaiter().GetResult();
        if (compilation is null)
        {
            return;
        }

        var assemblyName = compilation.AssemblyName ?? project.Name;
        AddNode(nodes, CreateAssemblyNode(assemblyName));

        foreach (var reference in project.ProjectReferences)
        {
            var referenced = project.Solution.GetProject(reference.ProjectId);
            if (referenced is null)
            {
                continue;
            }

            var referencedAssembly = referenced.AssemblyName ?? referenced.Name;
            AddNode(nodes, CreateAssemblyNode(referencedAssembly));
            AddEdge(edges, new GraphEdge(
                SymbolId.ForAssembly(assemblyName),
                SymbolId.ForAssembly(referencedAssembly),
                GraphRelation.ProjectReferences,
                GraphConfidence.Extracted,
                project.FilePath,
                null));
        }

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
            ExtractDeclaredSymbols(root, semanticModel, assemblyName, document.FilePath, nodes, edges);
            ExtractInvocations(root, semanticModel, assemblyName, document.FilePath, nodes, edges);
            ExtractTypeReferences(root, semanticModel, assemblyName, document.FilePath, nodes, edges);
        }

        MediatorGraphExtractor.Extract(project, compilation, assemblyName, nodes, edges);
        AspNetGraphExtractor.Extract(project, compilation, assemblyName, nodes, edges);
        DataGraphExtractor.Extract(project, compilation, assemblyName, nodes, edges);
        FileStorageGraphExtractor.Extract(project, compilation, assemblyName, nodes, edges);
        DiRegistrationGraphExtractor.Extract(project, compilation, assemblyName, nodes, edges);
        UiGraphExtractor.Extract(project, compilation, assemblyName, nodes, edges);
    }

    private static void ExtractDeclaredSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        string assemblyName,
        string? filePath,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        foreach (var typeDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            var symbol = semanticModel.GetDeclaredSymbol(typeDeclaration);
            if (symbol is null)
            {
                continue;
            }

            AddSymbolNode(nodes, symbol, assemblyName, filePath, typeDeclaration);
            AddContainmentEdges(nodes, edges, symbol, assemblyName, filePath);

            if (symbol.BaseType is { SpecialType: not SpecialType.System_Object } baseType)
            {
                AddSymbolNode(nodes, baseType, assemblyName, filePath, null);
                AddEdge(edges, new GraphEdge(
                    GetSymbolId(symbol, assemblyName),
                    GetSymbolId(baseType, assemblyName),
                    GraphRelation.Inherits,
                    GraphConfidence.Extracted,
                    filePath,
                    typeDeclaration.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
            }

            foreach (var iface in symbol.Interfaces)
            {
                AddSymbolNode(nodes, iface, assemblyName, filePath, null);
                AddEdge(edges, new GraphEdge(
                    GetSymbolId(symbol, assemblyName),
                    GetSymbolId(iface, assemblyName),
                    GraphRelation.Implements,
                    GraphConfidence.Extracted,
                    filePath,
                    typeDeclaration.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
            }

            foreach (var member in symbol.GetMembers())
            {
                AddSymbolNode(nodes, member, assemblyName, filePath, null);
                AddContainmentEdges(nodes, edges, member, assemblyName, filePath);

                if (member is IMethodSymbol method)
                {
                    ExtractMethodRelationships(method, assemblyName, filePath, nodes, edges);
                }
            }
        }
    }

    private static void ExtractMethodRelationships(
        IMethodSymbol method,
        string assemblyName,
        string? filePath,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        if (method.ReturnType is not null && method.ReturnType.SpecialType == SpecialType.None)
        {
            AddSymbolNode(nodes, method.ReturnType, assemblyName, filePath, null);
            AddEdge(edges, new GraphEdge(
                GetSymbolId(method, assemblyName),
                GetSymbolId(method.ReturnType, assemblyName),
                GraphRelation.Returns,
                GraphConfidence.Extracted,
                filePath,
                method.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line + 1));
        }

        foreach (var parameter in method.Parameters)
        {
            if (parameter.Type.SpecialType == SpecialType.None)
            {
                AddSymbolNode(nodes, parameter.Type, assemblyName, filePath, null);
                AddEdge(edges, new GraphEdge(
                    GetSymbolId(method, assemblyName),
                    GetSymbolId(parameter.Type, assemblyName),
                    GraphRelation.References,
                    GraphConfidence.Extracted,
                    filePath,
                    parameter.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line + 1));
            }
        }

        if (method.MethodKind == MethodKind.Constructor)
        {
            foreach (var parameter in method.Parameters)
            {
                if (parameter.Type.TypeKind is TypeKind.Class or TypeKind.Interface or TypeKind.Struct)
                {
                    AddSymbolNode(nodes, parameter.Type, assemblyName, filePath, null);
                    AddEdge(edges, new GraphEdge(
                        GetSymbolId(method.ContainingType, assemblyName),
                        GetSymbolId(parameter.Type, assemblyName),
                        GraphRelation.Injects,
                        GraphConfidence.Ambiguous,
                        filePath,
                        parameter.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line + 1,
                        """{"reason":"constructor_parameter_heuristic"}"""));
                }
            }
        }

        if (method.OverriddenMethod is not null)
        {
            AddSymbolNode(nodes, method.OverriddenMethod, assemblyName, filePath, null);
            AddEdge(edges, new GraphEdge(
                GetSymbolId(method, assemblyName),
                GetSymbolId(method.OverriddenMethod, assemblyName),
                GraphRelation.Overrides,
                GraphConfidence.Extracted,
                filePath,
                method.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line + 1));
        }
    }

    private static void ExtractInvocations(
        SyntaxNode root,
        SemanticModel semanticModel,
        string assemblyName,
        string? filePath,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var caller = RoslynGraphBuilderHelpers.GetEnclosingCallableSymbol(semanticModel, invocation);
            if (caller is null)
            {
                continue;
            }

            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            var callee = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
            if (callee is IMethodSymbol methodSymbol)
            {
                var confidence = symbolInfo.CandidateSymbols.Length > 1
                    ? GraphConfidence.Ambiguous
                    : GraphConfidence.Extracted;

                AddSymbolNode(nodes, caller, assemblyName, filePath, null);
                AddSymbolNode(nodes, methodSymbol, assemblyName, filePath, null);
                AddEdge(edges, new GraphEdge(
                    GetSymbolId(caller, assemblyName),
                    GetSymbolId(methodSymbol, assemblyName),
                    GraphRelation.Calls,
                    confidence,
                    filePath,
                    invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
            }
        }
    }

    private static void ExtractTypeReferences(
        SyntaxNode root,
        SemanticModel semanticModel,
        string assemblyName,
        string? filePath,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        foreach (var typeSyntax in root.DescendantNodes().OfType<TypeSyntax>())
        {
            if (typeSyntax.Parent is TypeDeclarationSyntax or BaseTypeSyntax or ParameterSyntax or PropertyDeclarationSyntax)
            {
                continue;
            }

            var typeInfo = semanticModel.GetTypeInfo(typeSyntax);
            var typeSymbol = typeInfo.Type;
            if (typeSymbol is null || typeSymbol.SpecialType != SpecialType.None)
            {
                continue;
            }

            var memberDeclaration = typeSyntax.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault();
            var enclosing = RoslynGraphBuilderHelpers.GetEnclosingCallableSymbol(semanticModel, typeSyntax)
                ?? (memberDeclaration is not null ? semanticModel.GetDeclaredSymbol(memberDeclaration) : null);
            if (enclosing is null)
            {
                continue;
            }

            AddSymbolNode(nodes, enclosing, assemblyName, filePath, null);
            AddSymbolNode(nodes, typeSymbol, assemblyName, filePath, null);
            AddEdge(edges, new GraphEdge(
                GetSymbolId(enclosing, assemblyName),
                GetSymbolId(typeSymbol, assemblyName),
                GraphRelation.References,
                GraphConfidence.Extracted,
                filePath,
                typeSyntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
        }
    }

    private static void AddContainmentEdges(
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges,
        ISymbol symbol,
        string assemblyName,
        string? filePath)
    {
        var symbolId = GetSymbolId(symbol, assemblyName);
        if (!string.IsNullOrEmpty(symbol.ContainingNamespace?.ToDisplayString()))
        {
            var namespaceName = symbol.ContainingNamespace.ToDisplayString();
            var namespaceId = SymbolId.ForNamespace(assemblyName, namespaceName);
            AddNode(nodes, new GraphNode(
                namespaceId,
                NodeKind.Namespace,
                namespaceName,
                namespaceName,
                assemblyName,
                filePath,
                null,
                null));

            AddEdge(edges, new GraphEdge(
                namespaceId,
                symbolId,
                GraphRelation.Contains,
                GraphConfidence.Extracted,
                filePath,
                symbol.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line + 1));
        }

        if (symbol.ContainingType is not null)
        {
            AddSymbolNode(nodes, symbol.ContainingType, assemblyName, filePath, null);
            AddEdge(edges, new GraphEdge(
                GetSymbolId(symbol.ContainingType, assemblyName),
                symbolId,
                GraphRelation.Contains,
                GraphConfidence.Extracted,
                filePath,
                symbol.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line + 1));
        }
        else if (symbol is INamedTypeSymbol)
        {
            var namespaceName = symbol.ContainingNamespace?.ToDisplayString();
            if (!string.IsNullOrEmpty(namespaceName))
            {
                AddEdge(edges, new GraphEdge(
                    SymbolId.ForNamespace(assemblyName, namespaceName),
                    symbolId,
                    GraphRelation.Contains,
                    GraphConfidence.Extracted,
                    filePath,
                    symbol.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line + 1));
            }
            else
            {
                AddEdge(edges, new GraphEdge(
                    SymbolId.ForAssembly(assemblyName),
                    symbolId,
                    GraphRelation.Contains,
                    GraphConfidence.Extracted,
                    filePath,
                    symbol.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line + 1));
            }
        }
    }

    private static void AddSymbolNode(
        Dictionary<string, GraphNode> nodes,
        ISymbol symbol,
        string assemblyName,
        string? filePath,
        SyntaxNode? declarationSyntax)
    {
        AddNode(nodes, CreateSymbolNode(symbol, assemblyName, filePath, declarationSyntax));
    }

    private static GraphNode CreateAssemblyNode(string assemblyName) =>
        new(
            SymbolId.ForAssembly(assemblyName),
            NodeKind.Assembly,
            assemblyName,
            assemblyName,
            assemblyName,
            null,
            null,
            null);

    private static GraphNode CreateSymbolNode(ISymbol symbol, string assemblyName, string? filePath, SyntaxNode? declarationSyntax)
    {
        var location = declarationSyntax?.GetLocation() ?? symbol.Locations.FirstOrDefault();
        var lineSpan = location?.GetLineSpan();
        return new GraphNode(
            GetSymbolId(symbol, assemblyName),
            GetNodeKind(symbol),
            symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            assemblyName,
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

    private static string GetSymbolId(ISymbol symbol, string assemblyName) =>
        SymbolId.ForSymbol(assemblyName, symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

    private static void AddNode(Dictionary<string, GraphNode> nodes, GraphNode node) =>
        nodes[node.Id] = node;

    private static void AddEdge(List<GraphEdge> edges, GraphEdge edge) => edges.Add(edge);
}
