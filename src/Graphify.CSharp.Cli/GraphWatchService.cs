using Graphify.CSharp.Core.Export;
using Graphify.CSharp.Core.Storage;
using Graphify.CSharp.Roslyn;

namespace Graphify.CSharp.Cli;

internal sealed class GraphWatchService
{
    public async Task WatchAsync(
        string solutionOrProjectPath,
        string databasePath,
        string? jsonOutputPath,
        int debounceMilliseconds = 1500,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(solutionOrProjectPath);
        var watchRoot = File.Exists(fullPath)
            ? Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory()
            : fullPath;

        Console.WriteLine($"Watching {watchRoot} for changes...");
        Console.WriteLine($"Database: {databasePath}");
        if (!string.IsNullOrWhiteSpace(jsonOutputPath))
        {
            Console.WriteLine($"JSON export: {jsonOutputPath}");
        }

        await RebuildAsync(fullPath, databasePath, jsonOutputPath, cancellationToken).ConfigureAwait(false);

        using var rebuildGate = new SemaphoreSlim(1, 1);
        Timer? debounceTimer = null;
        debounceTimer = new Timer(_ => _ = TriggerRebuildAsync());
        using var _ = debounceTimer;

        void ScheduleRebuild() => debounceTimer!.Change(debounceMilliseconds, Timeout.Infinite);

        async Task TriggerRebuildAsync()
        {
            if (!await rebuildGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                ScheduleRebuild();
                return;
            }

            try
            {
                await RebuildAsync(fullPath, databasePath, jsonOutputPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Graph rebuild failed: {ex.Message}");
            }
            finally
            {
                rebuildGate.Release();
            }
        }

        using var watcher = new FileSystemWatcher(watchRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };

        FileSystemEventHandler onChange = (_, args) =>
        {
            if (ShouldIgnore(args.FullPath))
            {
                return;
            }

            Console.WriteLine($"Change detected: {args.Name}");
            ScheduleRebuild();
        };

        watcher.Changed += onChange;
        watcher.Created += onChange;
        watcher.Deleted += onChange;
        watcher.Renamed += (_, args) =>
        {
            if (ShouldIgnore(args.FullPath))
            {
                return;
            }

            Console.WriteLine($"Rename detected: {args.Name}");
            ScheduleRebuild();
        };
        watcher.EnableRaisingEvents = true;

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Watch stopped.");
        }
    }

    private static async Task RebuildAsync(
        string solutionOrProjectPath,
        string databasePath,
        string? jsonOutputPath,
        CancellationToken cancellationToken)
    {
        var builder = new RoslynGraphBuilder();
        var snapshot = await builder.BuildAsync(solutionOrProjectPath, cancellationToken).ConfigureAwait(false);
        await using var database = await GraphDatabase.OpenAsync(databasePath, cancellationToken).ConfigureAwait(false);
        await database.ReplaceSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(jsonOutputPath))
        {
            await GraphJsonExporter.WriteAsync(snapshot, jsonOutputPath, cancellationToken).ConfigureAwait(false);
        }

        Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] Rebuilt {snapshot.Nodes.Count} nodes and {snapshot.Edges.Count} edges.");
    }

    private static bool ShouldIgnore(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        return path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}.graphify{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase));
    }
}
