namespace SampleApp.Web.Ui;

public abstract class UiPageBase
{
    protected readonly UiContainer Root = new();

    protected void Add(UiElement element) => Root.Add(element);
}

public sealed class UiContainer
{
    private readonly List<UiElement> _children = [];

    public void Add(UiElement element) => _children.Add(element);
}

public class UiElement
{
    public string? Id { get; set; }
    public string? Label { get; set; }
}

public sealed class UiFragment : UiElement;

public sealed class UiLink : UiElement;

public sealed class UiFileUpload : UiElement;

public sealed class UiCheckboxList : UiElement;

public static class Navigation
{
    public static void Redirect(string target) { }
}
