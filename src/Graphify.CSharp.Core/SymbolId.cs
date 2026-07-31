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
}
