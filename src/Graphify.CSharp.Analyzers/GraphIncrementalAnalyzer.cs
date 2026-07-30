using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Graphify.CSharp.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GraphIncrementalAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "GRAPHIFY001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Graphify incremental graph fragment",
        "Graphify captured {0} call edges in {1}",
        "Graphify",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        customTags: ["CompilationEnd"]);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var edgeCount = context.Compilation.SyntaxTrees
            .Select(tree => tree.GetRoot(context.CancellationToken))
            .SelectMany(root => root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Count();

        var assemblyName = context.Compilation.AssemblyName ?? "unknown";
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            Location.None,
            edgeCount,
            assemblyName));
    }
}
