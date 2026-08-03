namespace Graphify.CSharp.Core;

public static class SymbolId
{
    public static string ForAssembly(string assemblyName) => $"asm:{assemblyName}";

    public static string ForNamespace(string assemblyName, string namespaceName) =>
        $"ns:{assemblyName}|{namespaceName}";

    public static string ForSymbol(string assemblyName, string metadataName) =>
        $"sym:{assemblyName}|{metadataName}";

    public static string ForTable(string tableName) => $"table:{tableName.ToLowerInvariant()}";

    public static string ForPage(string relativePath) => $"page:{relativePath.Replace('\\', '/').ToLowerInvariant()}";

    public static string ForUiSurface(string key) => $"ui:surface|{NormalizeUiKey(key)}";

    public static string ForUiFragment(string surfaceKey, string fragmentKey) =>
        $"ui:fragment|{NormalizeUiKey(surfaceKey)}|{NormalizeUiKey(fragmentKey)}";

    public static string ForUiElement(string surfaceKey, string elementKey) =>
        $"ui:element|{NormalizeUiKey(surfaceKey)}|{NormalizeUiKey(elementKey)}";

    public static string ForUiGate(string surfaceKey, int line, string suffix) =>
        $"ui:gate|{NormalizeUiKey(surfaceKey)}|{line}|{NormalizeUiKey(suffix)}";

    public static string ForUiBinding(string expression) => $"ui:binding|{NormalizeUiKey(expression)}";

    public static string ForUiAction(string surfaceKey, int line, string name) =>
        $"ui:action|{NormalizeUiKey(surfaceKey)}|{line}|{NormalizeUiKey(name)}";

    public static string ForUiSelector(string surfaceKey, string selectorKey) =>
        $"ui:selector|{NormalizeUiKey(surfaceKey)}|{NormalizeUiKey(selectorKey)}";

    private static string NormalizeUiKey(string key) =>
        key.Replace('\\', '/').Trim().ToLowerInvariant();
}
