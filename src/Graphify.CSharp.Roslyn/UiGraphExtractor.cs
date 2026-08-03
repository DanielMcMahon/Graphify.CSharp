using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Graphify.CSharp.Core;
using Graphify.CSharp.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Graphify.CSharp.Roslyn;

internal static class UiGraphExtractor
{
    private static readonly string[] UiSurfaceBaseTypeHints =
    [
        "Page",
        "UserControl",
        "Component",
        "PageModel",
        "UiPageBase",
        "ViewComponent"
    ];

    private static readonly string[] UiElementTypeHints =
    [
        "Control",
        "Element",
        "Fragment",
        "Panel",
        "Well",
        "Link",
        "Button",
        "Input",
        "Upload",
        "Checkbox",
        "Select",
        "Field",
        "Widget"
    ];

    private static readonly string[] ContainerAddMethodNames =
    [
        "Add",
        "AddControl",
        "AddChild",
        "Append",
        "Register"
    ];

    private static readonly string[] NavigationMethodHints =
    [
        "Redirect",
        "Navigate",
        "GoTo",
        "Transfer"
    ];

    private static readonly string[] PermissionMethodHints =
    [
        "HasPermission",
        "IsInRole",
        "Authorize",
        "CanView",
        "CanEdit",
        "CanAccess",
        "CheckPermission",
        "Allow"
    ];

    private static readonly Regex SelectorAttributeRegex = new(
        @"(?:id|data-testid|data-test-id|aria-label)\s*=\s*[""']([^""']+)[""']",
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

            foreach (var typeDeclaration in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration);
                if (typeSymbol is null || !IsUiSurfaceType(typeSymbol, document.FilePath))
                {
                    continue;
                }

                var context = new UiExtractionContext(
                    assemblyName,
                    document.FilePath,
                    typeSymbol,
                    nodes,
                    edges);

                context.EnsureSurfaceNode();
                ExtractSurface(typeDeclaration, semanticModel, context);
            }
        }

        LinkPagesToSurfaces(nodes, edges);
    }

    private static void ExtractSurface(
        TypeDeclarationSyntax typeDeclaration,
        SemanticModel semanticModel,
        UiExtractionContext context)
    {
        foreach (var member in typeDeclaration.Members.OfType<MethodDeclarationSyntax>())
        {
            if (!IsUiRenderMethod(member.Identifier.Text))
            {
                continue;
            }

            var methodSymbol = semanticModel.GetDeclaredSymbol(member);
            if (methodSymbol is null || member.Body is null && member.ExpressionBody is null)
            {
                continue;
            }

            var body = member.Body;
            if (body is null)
            {
                continue;
            }

            foreach (var statement in body.Statements)
            {
                ProcessStatement(statement, semanticModel, context, context.SurfaceId, activeGates: []);
            }
        }
    }

    private static void ProcessStatement(
        StatementSyntax statement,
        SemanticModel semanticModel,
        UiExtractionContext context,
        string parentId,
        IReadOnlyList<string> activeGates)
    {
        switch (statement)
        {
            case BlockSyntax block:
                foreach (var inner in block.Statements)
                {
                    ProcessStatement(inner, semanticModel, context, parentId, activeGates);
                }

                break;
            case IfStatementSyntax ifStatement:
                ProcessIfStatement(ifStatement, semanticModel, context, parentId, activeGates);
                break;
            case LocalDeclarationStatementSyntax localDeclaration:
                ProcessLocalDeclaration(localDeclaration, semanticModel, context, parentId, activeGates);
                break;
            case ExpressionStatementSyntax expressionStatement:
                ProcessExpression(expressionStatement.Expression, semanticModel, context, parentId, activeGates, statement.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                break;
            case ReturnStatementSyntax returnStatement when returnStatement.Expression is not null:
                ProcessExpression(returnStatement.Expression, semanticModel, context, parentId, activeGates, statement.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                break;
        }
    }

    private static void ProcessIfStatement(
        IfStatementSyntax ifStatement,
        SemanticModel semanticModel,
        UiExtractionContext context,
        string parentId,
        IReadOnlyList<string> activeGates)
    {
        var line = ifStatement.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var expressionText = ifStatement.Condition.ToString();
        var gateKind = ClassifyGate(expressionText);
        var gateId = context.AddGate(line, expressionText, gateKind);
        LinkBindingsFromExpression(ifStatement.Condition, semanticModel, context, gateId);

        var gateChain = activeGates.Append(gateId).ToArray();
        if (ifStatement.Statement is not null)
        {
            WalkBlockOrStatement(ifStatement.Statement, semanticModel, context, parentId, gateChain);
        }

        if (ifStatement.Else?.Statement is not null)
        {
            WalkBlockOrStatement(ifStatement.Else.Statement, semanticModel, context, parentId, activeGates);
        }
    }

    private static void WalkBlockOrStatement(
        StatementSyntax statement,
        SemanticModel semanticModel,
        UiExtractionContext context,
        string parentId,
        IReadOnlyList<string> activeGates)
    {
        if (statement is BlockSyntax block)
        {
            foreach (var inner in block.Statements)
            {
                ProcessStatement(inner, semanticModel, context, parentId, activeGates);
            }

            return;
        }

        ProcessStatement(statement, semanticModel, context, parentId, activeGates);
    }

    private static void ProcessLocalDeclaration(
        LocalDeclarationStatementSyntax localDeclaration,
        SemanticModel semanticModel,
        UiExtractionContext context,
        string parentId,
        IReadOnlyList<string> activeGates)
    {
        foreach (var variable in localDeclaration.Declaration.Variables)
        {
            if (variable.Initializer?.Value is not ExpressionSyntax initializer)
            {
                continue;
            }

            var elementId = TryCreateUiNodeFromCreation(
                initializer,
                semanticModel,
                context,
                parentId,
                variable.Identifier.Text,
                localDeclaration.GetLocation().GetLineSpan().StartLinePosition.Line + 1);

            if (elementId is null)
            {
                ProcessExpression(initializer, semanticModel, context, parentId, activeGates, localDeclaration.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                continue;
            }

            ApplyGates(context, elementId, activeGates);
            ExtractSelectorsFromExpression(initializer, context, elementId);
        }
    }

    private static void ProcessExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        UiExtractionContext context,
        string parentId,
        IReadOnlyList<string> activeGates,
        int line)
    {
        if (expression is InvocationExpressionSyntax invocation)
        {
            ProcessInvocation(invocation, semanticModel, context, parentId, activeGates, line);
            return;
        }

        if (expression is AssignmentExpressionSyntax assignment)
        {
            ProcessAssignment(assignment, semanticModel, context, parentId, line);
            return;
        }

        if (expression is ObjectCreationExpressionSyntax creation)
        {
            var elementId = TryCreateUiNodeFromCreation(creation, semanticModel, context, parentId, creation.Type.ToString(), line);
            if (elementId is not null)
            {
                ApplyGates(context, elementId, activeGates);
                ExtractSelectorsFromExpression(creation, context, elementId);
            }
        }
    }

    private static void ProcessInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        UiExtractionContext context,
        string parentId,
        IReadOnlyList<string> activeGates,
        int line)
    {
        var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var methodName = symbol?.Name ?? invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => string.Empty
        };

        if (ContainerAddMethodNames.Contains(methodName, StringComparer.Ordinal)
            && invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is ExpressionSyntax addedExpression)
        {
            var elementId = TryCreateUiNodeFromCreation(addedExpression, semanticModel, context, parentId, addedExpression.ToString(), line)
                ?? TryCreateUiNodeFromIdentifier(addedExpression, context, parentId, line);

            if (elementId is not null)
            {
                ApplyGates(context, elementId, activeGates);
                ExtractSelectorsFromExpression(addedExpression, context, elementId);
            }

            return;
        }

        if (NavigationMethodHints.Any(hint => methodName.Contains(hint, StringComparison.OrdinalIgnoreCase)))
        {
            var target = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            var targetText = target is null ? methodName : target.ToString();
            var actionId = context.AddAction(line, methodName, targetText);
            context.AddEdge(parentId, actionId, GraphRelation.Contains);
            context.AddEdge(actionId, context.SurfaceId, GraphRelation.NavigatesTo, confidence: GraphConfidence.Ambiguous);

            var handler = RoslynGraphBuilderHelpers.GetEnclosingCallableSymbol(semanticModel, invocation);
            if (handler is not null)
            {
                var handlerId = RoslynGraphBuilderHelpers.GetSymbolId(handler, context.AssemblyName);
                RoslynGraphBuilderHelpers.AddSymbolNode(context.Nodes, handler, context.AssemblyName, context.FilePath, null);
                context.AddEdge(actionId, handlerId, GraphRelation.HandledBy);
            }

            return;
        }

        if (PermissionMethodHints.Any(hint => methodName.Contains(hint, StringComparison.OrdinalIgnoreCase)))
        {
            var gateId = context.AddGate(line, invocation.ToString(), "permission");
            LinkBindingsFromArguments(invocation, semanticModel, context, gateId);
        }
    }

    private static void ProcessAssignment(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        UiExtractionContext context,
        string parentId,
        int line)
    {
        var memberAccess = assignment.Left as MemberAccessExpressionSyntax;
        if (memberAccess is null)
        {
            return;
        }

        var propertyName = memberAccess.Name.Identifier.Text;
        if (propertyName.Equals("Visible", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
        {
            var gateId = context.AddGate(line, assignment.ToString(), "visibility");
            LinkBindingsFromExpression(assignment.Right, semanticModel, context, gateId);
        }

        if (propertyName.Equals("Id", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("ClientID", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("CssClass", StringComparison.OrdinalIgnoreCase))
        {
            var selectorValue = RoslynGraphBuilderHelpers.TryGetConstantString(assignment.Right);
            if (!string.IsNullOrWhiteSpace(selectorValue))
            {
                var targetName = memberAccess.Expression.ToString();
                var elementId = context.FindElementByName(targetName) ?? parentId;
                context.AddSelector(elementId, propertyName.ToLowerInvariant(), selectorValue);
            }
        }
    }

    private static string? TryCreateUiNodeFromCreation(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        UiExtractionContext context,
        string parentId,
        string fallbackName,
        int line)
    {
        if (expression is not ObjectCreationExpressionSyntax creation)
        {
            return null;
        }

        var typeName = creation.Type.ToString();
        if (!LooksLikeUiType(typeName))
        {
            return null;
        }

        var name = ExtractNameFromInitializer(creation) ?? fallbackName;
        var kind = typeName.Contains("Fragment", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Panel", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Well", StringComparison.OrdinalIgnoreCase)
            ? NodeKind.UiFragment
            : NodeKind.UiElement;

        var nodeId = context.AddUiNode(kind, name, typeName, line);
        context.AddEdge(parentId, nodeId, GraphRelation.Renders);
        context.AddEdge(parentId, nodeId, GraphRelation.Contains);
        ExtractSelectorsFromExpression(creation, context, nodeId);
        return nodeId;
    }

    private static string? TryCreateUiNodeFromIdentifier(
        ExpressionSyntax expression,
        UiExtractionContext context,
        string parentId,
        int line)
    {
        var name = expression.ToString();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var nodeId = context.AddUiNode(NodeKind.UiElement, name, "reference", line);
        context.AddEdge(parentId, nodeId, GraphRelation.Renders);
        context.AddEdge(parentId, nodeId, GraphRelation.Contains);
        return nodeId;
    }

    private static void ApplyGates(UiExtractionContext context, string elementId, IReadOnlyList<string> activeGates)
    {
        foreach (var gateId in activeGates)
        {
            context.AddEdge(elementId, gateId, GraphRelation.GatedBy);
        }
    }

    private static void ExtractSelectorsFromExpression(
        ExpressionSyntax expression,
        UiExtractionContext context,
        string elementId)
    {
        foreach (var literal in expression.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>())
        {
            if (!literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                continue;
            }

            var text = literal.Token.ValueText;
            var match = SelectorAttributeRegex.Match(text);
            if (match.Success)
            {
                context.AddSelector(elementId, "attribute", match.Groups[1].Value);
                continue;
            }

            if (text.Length is > 0 and <= 80
                && (text.Contains("well", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("btn", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("link", StringComparison.OrdinalIgnoreCase)))
            {
                context.AddSelector(elementId, "label", text);
            }
        }

        foreach (var assignment in expression.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is IdentifierNameSyntax identifier)
            {
                var name = identifier.Identifier.Text;
                if (name.Equals("Id", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Label", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("AriaLabel", StringComparison.OrdinalIgnoreCase))
                {
                    var value = RoslynGraphBuilderHelpers.TryGetConstantString(assignment.Right);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        context.AddSelector(elementId, name.ToLowerInvariant(), value);
                    }
                }
            }
            else
            {
                ProcessAssignment(assignment, null!, context, elementId, assignment.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
            }
        }
    }

    private static void LinkBindingsFromExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        UiExtractionContext context,
        string gateId)
    {
        foreach (var memberAccess in expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            var symbol = semanticModel.GetSymbolInfo(memberAccess).Symbol;
            if (symbol is IPropertySymbol or IFieldSymbol)
            {
                var bindingId = context.AddBinding(memberAccess.ToString(), symbol.ToDisplayString());
                context.AddEdge(gateId, bindingId, GraphRelation.BoundTo);
            }
        }
    }

    private static void LinkBindingsFromArguments(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        UiExtractionContext context,
        string gateId)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            LinkBindingsFromExpression(argument.Expression, semanticModel, context, gateId);
        }
    }

    private static string? ExtractNameFromInitializer(ObjectCreationExpressionSyntax creation)
    {
        if (creation.Initializer is null)
        {
            return null;
        }

        foreach (var expression in creation.Initializer.Expressions)
        {
            if (expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax identifier })
            {
                if (identifier.Identifier.Text is "Id" or "Name" or "Label")
                {
                    return RoslynGraphBuilderHelpers.TryGetConstantString(((AssignmentExpressionSyntax)expression).Right);
                }
            }
        }

        return null;
    }

    private static string ClassifyGate(string expressionText)
    {
        if (PermissionMethodHints.Any(hint => expressionText.Contains(hint, StringComparison.OrdinalIgnoreCase)))
        {
            return "permission";
        }

        if (expressionText.Contains("null", StringComparison.OrdinalIgnoreCase))
        {
            return "null_check";
        }

        if (expressionText.Contains("Status", StringComparison.OrdinalIgnoreCase)
            || expressionText.Contains("State", StringComparison.OrdinalIgnoreCase))
        {
            return "entity_state";
        }

        return "condition";
    }

    private static bool IsUiSurfaceType(INamedTypeSymbol typeSymbol, string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var normalized = filePath.Replace('\\', '/');
            if (normalized.EndsWith(".aspx.cs", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".ascx.cs", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (typeSymbol.Name.EndsWith("Page", StringComparison.OrdinalIgnoreCase)
            || typeSymbol.Name.EndsWith("Screen", StringComparison.OrdinalIgnoreCase)
            || typeSymbol.Name.EndsWith("View", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        for (var current = typeSymbol.BaseType; current is not null; current = current.BaseType)
        {
            if (UiSurfaceBaseTypeHints.Any(hint => current.Name.Contains(hint, StringComparison.OrdinalIgnoreCase)))
            {
                return typeSymbol.GetMembers().OfType<IMethodSymbol>().Any(method => IsUiRenderMethod(method.Name));
            }
        }

        return false;
    }

    private static bool IsUiRenderMethod(string methodName) =>
        methodName.Equals("Page_Load", StringComparison.OrdinalIgnoreCase)
        || methodName.StartsWith("OnGet", StringComparison.OrdinalIgnoreCase)
        || methodName.StartsWith("OnPost", StringComparison.OrdinalIgnoreCase)
        || methodName.StartsWith("Render", StringComparison.OrdinalIgnoreCase)
        || methodName.StartsWith("Build", StringComparison.OrdinalIgnoreCase)
        || methodName.Equals("Setup", StringComparison.OrdinalIgnoreCase)
        || methodName.StartsWith("Initialize", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeUiType(string typeName) =>
        UiElementTypeHints.Any(hint => typeName.Contains(hint, StringComparison.OrdinalIgnoreCase));

    private static void LinkPagesToSurfaces(Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
    {
        var surfaces = nodes.Values.Where(node => node.Kind == NodeKind.UiSurface).ToList();
        var pages = nodes.Values.Where(node => node.Kind == NodeKind.Page).ToList();

        foreach (var surface in surfaces)
        {
            if (string.IsNullOrWhiteSpace(surface.FilePath))
            {
                continue;
            }

            var pageStem = Path.GetFileNameWithoutExtension(surface.FilePath);
            if (pageStem.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase))
            {
                pageStem = Path.GetFileNameWithoutExtension(pageStem);
            }

            var page = pages.FirstOrDefault(candidate =>
                string.Equals(Path.GetFileNameWithoutExtension(candidate.FilePath), pageStem, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Name, pageStem, StringComparison.OrdinalIgnoreCase));

            if (page is null)
            {
                continue;
            }

            RoslynGraphBuilderHelpers.AddEdge(edges, new GraphEdge(
                page.Id,
                surface.Id,
                GraphRelation.Hosts,
                GraphConfidence.Extracted,
                surface.FilePath,
                surface.Line));
        }
    }

    private sealed class UiExtractionContext
    {
        private readonly Dictionary<string, string> _elementNames = new(StringComparer.OrdinalIgnoreCase);
        private int _sequence;

        public UiExtractionContext(
            string assemblyName,
            string? filePath,
            INamedTypeSymbol typeSymbol,
            Dictionary<string, GraphNode> nodes,
            List<GraphEdge> edges)
        {
            AssemblyName = assemblyName;
            FilePath = filePath;
            TypeSymbol = typeSymbol;
            Nodes = nodes;
            Edges = edges;
            SurfaceKey = typeSymbol.ToDisplayString();
            SurfaceId = SymbolId.ForUiSurface(SurfaceKey);
        }

        public string AssemblyName { get; }
        public string? FilePath { get; }
        public INamedTypeSymbol TypeSymbol { get; }
        public Dictionary<string, GraphNode> Nodes { get; }
        public List<GraphEdge> Edges { get; }
        public string SurfaceKey { get; }
        public string SurfaceId { get; }

        public void EnsureSurfaceNode()
        {
            RoslynGraphBuilderHelpers.AddGraphNode(Nodes, new GraphNode(
                SurfaceId,
                NodeKind.UiSurface,
                TypeSymbol.Name,
                TypeSymbol.ToDisplayString(),
                AssemblyName,
                FilePath,
                TypeSymbol.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line + 1,
                null,
                JsonSerializer.Serialize(new { role = "ui_surface" })));

            var typeId = RoslynGraphBuilderHelpers.GetSymbolId(TypeSymbol, AssemblyName);
            RoslynGraphBuilderHelpers.AddSymbolNode(Nodes, TypeSymbol, AssemblyName, FilePath, null);
            AddEdge(SurfaceId, typeId, GraphRelation.HandledBy);
        }

        public string AddUiNode(NodeKind kind, string name, string typeName, int line)
        {
            var key = $"{name}|{++_sequence}";
            var id = kind == NodeKind.UiFragment
                ? SymbolId.ForUiFragment(SurfaceKey, key)
                : SymbolId.ForUiElement(SurfaceKey, key);

            RoslynGraphBuilderHelpers.AddGraphNode(Nodes, new GraphNode(
                id,
                kind,
                name,
                typeName,
                AssemblyName,
                FilePath,
                line,
                null,
                JsonSerializer.Serialize(new { uiType = typeName })));

            _elementNames[name] = id;
            return id;
        }

        public string AddGate(int line, string expression, string gateKind)
        {
            var suffix = Convert.ToHexString(SHA1Hash(expression));
            var id = SymbolId.ForUiGate(SurfaceKey, line, suffix);
            RoslynGraphBuilderHelpers.AddGraphNode(Nodes, new GraphNode(
                id,
                NodeKind.UiGate,
                gateKind,
                expression,
                AssemblyName,
                FilePath,
                line,
                null,
                JsonSerializer.Serialize(new { gateKind, expression })));

            return id;
        }

        public string AddBinding(string expression, string symbolDisplay)
        {
            var id = SymbolId.ForUiBinding(expression);
            RoslynGraphBuilderHelpers.AddGraphNode(Nodes, new GraphNode(
                id,
                NodeKind.UiBinding,
                expression,
                symbolDisplay,
                AssemblyName,
                FilePath,
                null,
                null,
                JsonSerializer.Serialize(new { expression, symbol = symbolDisplay })));

            return id;
        }

        public string AddAction(int line, string name, string target)
        {
            var id = SymbolId.ForUiAction(SurfaceKey, line, name);
            RoslynGraphBuilderHelpers.AddGraphNode(Nodes, new GraphNode(
                id,
                NodeKind.UiAction,
                name,
                target,
                AssemblyName,
                FilePath,
                line,
                null,
                JsonSerializer.Serialize(new { target })));

            return id;
        }

        public void AddSelector(string elementId, string selectorKind, string value)
        {
            var id = SymbolId.ForUiSelector(SurfaceKey, $"{selectorKind}:{value}");
            RoslynGraphBuilderHelpers.AddGraphNode(Nodes, new GraphNode(
                id,
                NodeKind.UiSelectorHint,
                value,
                $"{selectorKind}:{value}",
                AssemblyName,
                FilePath,
                null,
                null,
                JsonSerializer.Serialize(new { selectorKind, value, confidence = "extracted" })));

            AddEdge(elementId, id, GraphRelation.EmitsSelector);
        }

        public string? FindElementByName(string name) =>
            _elementNames.TryGetValue(name, out var id) ? id : null;

        public void AddEdge(string sourceId, string targetId, string relation, GraphConfidence confidence = GraphConfidence.Extracted) =>
            RoslynGraphBuilderHelpers.AddEdge(Edges, new GraphEdge(
                sourceId,
                targetId,
                relation,
                confidence,
                FilePath,
                null));

        private static byte[] SHA1Hash(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            return System.Security.Cryptography.SHA1.HashData(bytes);
        }
    }
}
