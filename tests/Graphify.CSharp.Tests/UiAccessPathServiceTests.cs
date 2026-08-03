using Graphify.CSharp.Core.Models;
using Graphify.CSharp.Core.Query;
using Graphify.CSharp.Core.Storage;
using Graphify.CSharp.Roslyn;

namespace Graphify.CSharp.Tests;

public class UiAccessPathServiceTests
{
    private static string SampleSolutionPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "SampleApp", "SampleApp.sln"));

    [Fact]
    public async Task Build_extracts_ui_surfaces_elements_gates_and_selectors()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"graphify-ui-{Guid.NewGuid():N}.db");
        var snapshot = await new RoslynGraphBuilder().BuildAsync(SampleSolutionPath);

        await using var database = await GraphDatabase.OpenAsync(databasePath);
        await database.ReplaceSnapshotAsync(snapshot);

        Assert.Contains(snapshot.Nodes, node => node.Kind == NodeKind.UiSurface && node.Name == "OrderDetailsPage");
        Assert.Contains(snapshot.Nodes, node => node.Kind == NodeKind.UiElement && node.Name == "invoice-link");
        Assert.Contains(snapshot.Nodes, node => node.Kind == NodeKind.UiFragment && node.Name == "invoice-well");
        Assert.Contains(snapshot.Nodes, node => node.Kind == NodeKind.UiGate);
        Assert.Contains(snapshot.Nodes, node => node.Kind == NodeKind.UiSelectorHint && node.Name == "invoice-link");
        Assert.Contains(snapshot.Edges, edge => edge.Relation == GraphRelation.GatedBy);
        Assert.Contains(snapshot.Edges, edge => edge.Relation == GraphRelation.BoundTo);
        Assert.Contains(snapshot.Edges, edge => edge.Relation == GraphRelation.Renders);
    }

    [Fact]
    public async Task GetAccessPath_returns_prerequisites_for_invoice_link()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"graphify-ui-path-{Guid.NewGuid():N}.db");
        var snapshot = await new RoslynGraphBuilder().BuildAsync(SampleSolutionPath);

        await using var database = await GraphDatabase.OpenAsync(databasePath);
        await database.ReplaceSnapshotAsync(snapshot);

        var service = new UiAccessPathService();
        var result = await service.GetAccessPathAsync(database, "invoice-link", "OrderDetailsPage");

        Assert.NotNull(result.Target);
        Assert.NotNull(result.Surface);
        Assert.Contains(result.Prerequisites, p => p.Contains("ViewInvoices", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Prerequisites, p => p.Contains("Invoice", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Selectors, selector => selector.Value == "invoice-link");
        Assert.Contains("invoice-link", result.Markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSurfaceMap_lists_rendered_elements()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"graphify-ui-map-{Guid.NewGuid():N}.db");
        var snapshot = await new RoslynGraphBuilder().BuildAsync(SampleSolutionPath);

        await using var database = await GraphDatabase.OpenAsync(databasePath);
        await database.ReplaceSnapshotAsync(snapshot);

        var service = new UiAccessPathService();
        var result = await service.GetSurfaceMapAsync(database, "OrderDetailsPage");

        Assert.Equal("OrderDetailsPage", result.Surface.Name);
        Assert.Contains(result.Elements, element => element.Name == "order-attachment-upload");
        Assert.Contains(result.Fragments, fragment => fragment.Name == "invoice-well");
        Assert.Contains("OrderDetailsPage", result.Markdown, StringComparison.OrdinalIgnoreCase);
    }
}
