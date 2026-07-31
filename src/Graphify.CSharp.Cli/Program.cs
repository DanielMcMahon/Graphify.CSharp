using Graphify.CSharp.Core;
using Graphify.CSharp.Core.Export;
using Graphify.CSharp.Core.Query;
using Graphify.CSharp.Core.Storage;
using Graphify.CSharp.Core.Workspace;
using Graphify.CSharp.Roslyn;
using Graphify.CSharp.Web;
using System.CommandLine;

namespace Graphify.CSharp.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var root = new RootCommand("Build and query a Roslyn-powered C# knowledge graph.");

        var buildCommand = new Command("build", "Analyze a solution or project and persist the knowledge graph.");
        var buildPathArg = new Argument<string>("path", "Path to a .sln or .csproj file.");
        var buildOutputOption = new Option<string>("--output", () => ".graphify/graph.db", "SQLite database output path.");
        var buildJsonOption = new Option<string?>("--json", () => null, "Optional Graphify-compatible JSON export path.");
        buildCommand.AddArgument(buildPathArg);
        buildCommand.AddOption(buildOutputOption);
        buildCommand.AddOption(buildJsonOption);
        buildCommand.SetHandler(BuildAsync, buildPathArg, buildOutputOption, buildJsonOption);
        root.AddCommand(buildCommand);

        var queryCommand = new Command("query", "Search graph nodes by name or fully-qualified symbol.");
        var queryDbOption = new Option<string>("--db", () => ".graphify/graph.db", "SQLite database path.");
        var queryTextArg = new Argument<string>("text", "Search text.");
        queryCommand.AddOption(queryDbOption);
        queryCommand.AddArgument(queryTextArg);
        queryCommand.SetHandler(QueryAsync, queryDbOption, queryTextArg);
        root.AddCommand(queryCommand);

        var pathCommand = new Command("path", "Find knowledge paths between two symbols.");
        var pathDbOption = new Option<string>("--db", () => ".graphify/graph.db", "SQLite database path.");
        var fromArg = new Argument<string>("from", "Source symbol query.");
        var toArg = new Argument<string>("to", "Target symbol query.");
        pathCommand.AddOption(pathDbOption);
        pathCommand.AddArgument(fromArg);
        pathCommand.AddArgument(toArg);
        pathCommand.SetHandler(PathAsync, pathDbOption, fromArg, toArg);
        root.AddCommand(pathCommand);

        var explainCommand = new Command("explain", "Show incoming and outgoing relationships for a symbol.");
        var explainDbOption = new Option<string>("--db", () => ".graphify/graph.db", "SQLite database path.");
        var explainArg = new Argument<string>("symbol", "Symbol query.");
        explainCommand.AddOption(explainDbOption);
        explainCommand.AddArgument(explainArg);
        explainCommand.SetHandler(ExplainAsync, explainDbOption, explainArg);
        root.AddCommand(explainCommand);

        var gapsCommand = new Command("gaps", "Suggest likely gaps when no path exists between symbols.");
        var gapsDbOption = new Option<string>("--db", () => ".graphify/graph.db", "SQLite database path.");
        var gapsFromArg = new Argument<string>("from", "Source symbol query.");
        var gapsToArg = new Argument<string>("to", "Target symbol query.");
        gapsCommand.AddOption(gapsDbOption);
        gapsCommand.AddArgument(gapsFromArg);
        gapsCommand.AddArgument(gapsToArg);
        gapsCommand.SetHandler(GapsAsync, gapsDbOption, gapsFromArg, gapsToArg);
        root.AddCommand(gapsCommand);

        var serveCommand = new Command("serve", "Launch the interactive graph UI in your browser.");
        var serveDbOption = new Option<string>("--db", () => ".graphify/graph.db", "SQLite database path.");
        var servePortOption = new Option<int>("--port", () => 5173, "HTTP port.");
        var serveHostOption = new Option<string>("--host", () => "127.0.0.1", "HTTP host.");
        serveCommand.AddOption(serveDbOption);
        serveCommand.AddOption(servePortOption);
        serveCommand.AddOption(serveHostOption);
        serveCommand.SetHandler(ServeAsync, serveDbOption, servePortOption, serveHostOption);
        root.AddCommand(serveCommand);

        var exportCommand = new Command("export", "Export a graph database to Graphify-compatible JSON.");
        var exportDbOption = new Option<string>("--db", () => ".graphify/graph.db", "SQLite database path.");
        var exportOutputOption = new Option<string>("--output", () => ".graphify/graph.json", "JSON output path.");
        exportCommand.AddOption(exportDbOption);
        exportCommand.AddOption(exportOutputOption);
        exportCommand.SetHandler(ExportAsync, exportDbOption, exportOutputOption);
        root.AddCommand(exportCommand);

        var watchCommand = new Command("watch", "Rebuild the graph when source files change.");
        var watchPathArg = new Argument<string>("path", "Path to a .sln or .csproj file.");
        var watchOutputOption = new Option<string>("--output", () => ".graphify/graph.db", "SQLite database output path.");
        var watchJsonOption = new Option<string?>("--json", () => null, "Optional Graphify-compatible JSON export path.");
        var watchDebounceOption = new Option<int>("--debounce", () => 1500, "Debounce interval in milliseconds.");
        watchCommand.AddArgument(watchPathArg);
        watchCommand.AddOption(watchOutputOption);
        watchCommand.AddOption(watchJsonOption);
        watchCommand.AddOption(watchDebounceOption);
        watchCommand.SetHandler(WatchAsync, watchPathArg, watchOutputOption, watchJsonOption, watchDebounceOption);
        root.AddCommand(watchCommand);

        var flowsCommand = new Command("flows", "Find ASP.NET endpoint to repository flow paths.");
        var flowsDbOption = new Option<string>("--db", () => ".graphify/graph.db", "SQLite database path.");
        var flowsEndpointArg = new Argument<string>("endpoint", "Endpoint route or handler query.");
        flowsCommand.AddOption(flowsDbOption);
        flowsCommand.AddArgument(flowsEndpointArg);
        flowsCommand.SetHandler(FlowsAsync, flowsDbOption, flowsEndpointArg);
        root.AddCommand(flowsCommand);

        var installCommand = new Command("install", "Install Graphify.CSharp skills and MCP config for AI agents.");
        var installAllOption = new Option<bool>("--all", () => false, "Install skill + MCP for Cursor, Copilot, and OpenCode.");
        var installClaudeSkillOption = new Option<bool>("--claude-skill", () => false, "Install Claude Code agent skill.");
        var installCursorSkillOption = new Option<bool>("--cursor-skill", () => false, "Install Cursor agent skill.");
        var installCursorMcpOption = new Option<bool>("--cursor-mcp", () => false, "Add MCP server to Cursor config.");
        var installCopilotMcpOption = new Option<bool>("--copilot-mcp", () => false, "Add MCP server to Copilot config.");
        var installOpenCodeMcpOption = new Option<bool>("--opencode-mcp", () => false, "Add MCP server to OpenCode config.");
        var installProjectOption = new Option<bool>("--project", () => false, "Install into current project instead of global config.");
        var installDbOption = new Option<string>("--db", () => ".graphify/graph.db", "Default GRAPHIFY_DB path written into MCP config.");
        installCommand.AddOption(installAllOption);
        installCommand.AddOption(installClaudeSkillOption);
        installCommand.AddOption(installCursorSkillOption);
        installCommand.AddOption(installCursorMcpOption);
        installCommand.AddOption(installCopilotMcpOption);
        installCommand.AddOption(installOpenCodeMcpOption);
        installCommand.AddOption(installProjectOption);
        installCommand.AddOption(installDbOption);
        installCommand.SetHandler(InstallAsync, installAllOption, installClaudeSkillOption, installCursorSkillOption, installCursorMcpOption, installCopilotMcpOption, installOpenCodeMcpOption, installProjectOption, installDbOption);
        root.AddCommand(installCommand);

        var initCommand = new Command("init", "One-command setup: build graph, install agent skill/MCP/rule, and enable auto-refresh.");
        var initSolutionOption = new Option<string?>("--solution", () => null, "Path to a .sln file (auto-detected when omitted).");
        var initDbOption = new Option<string?>("--db", () => null, "SQLite database path (defaults to .graphify/graph.db).");
        var initGlobalOption = new Option<bool>("--global", () => false, "Also install global agent integrations.");
        var initNoHookOption = new Option<bool>("--no-hook", () => false, "Skip git post-commit auto-rebuild hook.");
        initCommand.AddOption(initSolutionOption);
        initCommand.AddOption(initDbOption);
        initCommand.AddOption(initGlobalOption);
        initCommand.AddOption(initNoHookOption);
        initCommand.SetHandler(InitAsync, initSolutionOption, initDbOption, initGlobalOption, initNoHookOption);
        root.AddCommand(initCommand);

        var ensureGraphCommand = new Command("ensure-graph", "Build or rebuild the graph when missing or stale.");
        var ensureProjectRootOption = new Option<string?>("--project-root", () => null, "Project root containing .graphify/config.json.");
        ensureGraphCommand.AddOption(ensureProjectRootOption);
        ensureGraphCommand.SetHandler(EnsureGraphAsync, ensureProjectRootOption);
        root.AddCommand(ensureGraphCommand);

        var statusCommand = new Command("status", "Show graph workspace status.");
        var statusProjectRootOption = new Option<string?>("--project-root", () => null, "Project root containing .graphify/config.json.");
        statusCommand.AddOption(statusProjectRootOption);
        statusCommand.SetHandler(StatusAsync, statusProjectRootOption);
        root.AddCommand(statusCommand);

        var howCommand = new Command("how", "Explain how a symbol or feature works.");
        var howDbOption = new Option<string>("--db", () => ".graphify/graph.db", "SQLite database path.");
        var howTopicArg = new Argument<string>("topic", "Symbol, class, method, or feature name.");
        howCommand.AddOption(howDbOption);
        howCommand.AddArgument(howTopicArg);
        howCommand.SetHandler(HowAsync, howDbOption, howTopicArg);
        root.AddCommand(howCommand);

        var investigateCommand = new Command("investigate", "Run a full architecture investigation for any question or feature.");
        var investigateProjectRootOption = new Option<string?>("--project-root", () => null, "Project root containing .graphify/config.json.");
        var investigateNoHandoffOption = new Option<bool>("--no-handoff", () => false, "Skip writing .graphify/investigations/<topic>.md.");
        var investigateQuestionArg = new Argument<string>("question", "Natural language question or symbol name.");
        investigateCommand.AddOption(investigateProjectRootOption);
        investigateCommand.AddOption(investigateNoHandoffOption);
        investigateCommand.AddArgument(investigateQuestionArg);
        investigateCommand.SetHandler(InvestigateAsync, investigateProjectRootOption, investigateNoHandoffOption, investigateQuestionArg);
        root.AddCommand(investigateCommand);

        var assessCommand = new Command("assess", "Assess removal difficulty or architecture swaps (e.g. MediatR to direct handler calls).");
        var assessDbOption = new Option<string>("--db", () => ".graphify/graph.db", "SQLite database path.");
        var assessTopicArg = new Argument<string>("topic", "Symbol to remove, or 'mediator' / 'mediatr' for a MediatR swap assessment.");
        assessCommand.AddOption(assessDbOption);
        assessCommand.AddArgument(assessTopicArg);
        assessCommand.SetHandler(AssessAsync, assessDbOption, assessTopicArg);
        root.AddCommand(assessCommand);

        var traceTableCommand = new Command("trace-table", "Trace backwards from a database table to pages, file columns, and storage touchpoints.");
        var traceTableProjectRootOption = new Option<string?>("--project-root", () => null, "Project root containing .graphify/config.json.");
        var traceTableNameArg = new Argument<string>("table", "Database table name.");
        traceTableCommand.AddOption(traceTableProjectRootOption);
        traceTableCommand.AddArgument(traceTableNameArg);
        traceTableCommand.SetHandler(TraceTableAsync, traceTableProjectRootOption, traceTableNameArg);
        root.AddCommand(traceTableCommand);

        return await root.InvokeAsync(args).ConfigureAwait(false);
    }

    private static async Task BuildAsync(string path, string output, string? jsonOutput)
    {
        Console.WriteLine($"Building graph from {path}...");
        var builder = new RoslynGraphBuilder();
        var snapshot = await builder.BuildAsync(path).ConfigureAwait(false);

        await using var database = await GraphDatabase.OpenAsync(output).ConfigureAwait(false);
        await database.ReplaceSnapshotAsync(snapshot).ConfigureAwait(false);

        Console.WriteLine($"Wrote {snapshot.Nodes.Count} nodes and {snapshot.Edges.Count} edges to {output}");

        if (!string.IsNullOrWhiteSpace(jsonOutput))
        {
            await GraphJsonExporter.WriteAsync(snapshot, jsonOutput).ConfigureAwait(false);
            Console.WriteLine($"Exported JSON to {jsonOutput}");
        }
    }

    private static async Task QueryAsync(string dbPath, string text)
    {
        await using var database = await GraphDatabase.OpenAsync(dbPath).ConfigureAwait(false);
        var nodes = await database.SearchNodesAsync(text).ConfigureAwait(false);

        foreach (var node in nodes)
        {
            Console.WriteLine($"{node.Kind,-10} {node.FullName ?? node.Name}");
            if (!string.IsNullOrEmpty(node.FilePath))
            {
                Console.WriteLine($"           {node.FilePath}:{node.Line}");
            }
        }
    }

    private static async Task PathAsync(string dbPath, string from, string to)
    {
        await using var database = await GraphDatabase.OpenAsync(dbPath).ConfigureAwait(false);
        var service = new GraphQueryService();
        var paths = await service.FindPathsAsync(database, from, to).ConfigureAwait(false);

        if (paths.Count == 0)
        {
            Console.WriteLine("No path found.");
            return;
        }

        var index = 1;
        foreach (var path in paths)
        {
            Console.WriteLine($"Path {index++}:");
            foreach (var step in path.Steps)
            {
                if (step.IncomingEdge is null)
                {
                    Console.WriteLine($"  {step.Node.FullName ?? step.Node.Name}");
                    continue;
                }

                Console.WriteLine($"  --[{step.IncomingEdge.Relation}/{step.IncomingEdge.Confidence}]--> {step.Node.FullName ?? step.Node.Name}");
                if (!string.IsNullOrEmpty(step.IncomingEdge.SourceFile))
                {
                    Console.WriteLine($"      at {step.IncomingEdge.SourceFile}:{step.IncomingEdge.Line}");
                }
            }

            Console.WriteLine();
        }
    }

    private static async Task ExplainAsync(string dbPath, string symbol)
    {
        await using var database = await GraphDatabase.OpenAsync(dbPath).ConfigureAwait(false);
        var service = new GraphQueryService();
        var explanation = await service.ExplainAsync(database, symbol).ConfigureAwait(false);

        if (explanation.Node is null)
        {
            Console.WriteLine("Symbol not found.");
            return;
        }

        Console.WriteLine(explanation.Node.FullName ?? explanation.Node.Name);
        Console.WriteLine("Incoming:");
        foreach (var edge in explanation.Incoming)
        {
            var source = await database.GetNodeAsync(edge.SourceId).ConfigureAwait(false);
            Console.WriteLine($"  {edge.Relation} <- {source?.FullName ?? edge.SourceId} ({edge.Confidence})");
        }

        Console.WriteLine("Outgoing:");
        foreach (var edge in explanation.Outgoing)
        {
            var target = await database.GetNodeAsync(edge.TargetId).ConfigureAwait(false);
            Console.WriteLine($"  {edge.Relation} -> {target?.FullName ?? edge.TargetId} ({edge.Confidence})");
        }
    }

    private static async Task GapsAsync(string dbPath, string from, string to)
    {
        await using var database = await GraphDatabase.OpenAsync(dbPath).ConfigureAwait(false);
        var service = new GraphQueryService();
        var lines = await service.FindGapsAsync(database, from, to).ConfigureAwait(false);
        foreach (var line in lines)
        {
            Console.WriteLine(line);
        }
    }

    private static Task ServeAsync(string dbPath, int port, string host) =>
        GraphWebHost.RunAsync(["--db", dbPath, "--port", port.ToString(), "--host", host]);

    private static async Task ExportAsync(string dbPath, string jsonOutput)
    {
        await using var database = await GraphDatabase.OpenAsync(dbPath).ConfigureAwait(false);
        var snapshot = await database.LoadSnapshotAsync().ConfigureAwait(false);
        await GraphJsonExporter.WriteAsync(snapshot, jsonOutput).ConfigureAwait(false);
        Console.WriteLine($"Exported {snapshot.Nodes.Count} nodes and {snapshot.Edges.Count} edges to {jsonOutput}");
    }

    private static Task WatchAsync(string path, string output, string? jsonOutput, int debounce) =>
        new GraphWatchService().WatchAsync(path, output, jsonOutput, debounce);

    private static Task InstallAsync(
        bool all,
        bool claudeSkill,
        bool cursorSkill,
        bool cursorMcp,
        bool copilotMcp,
        bool openCodeMcp,
        bool project,
        string dbPath)
    {
        var installEverything = all || (!claudeSkill && !cursorSkill && !cursorMcp && !copilotMcp && !openCodeMcp);
        var options = new InstallOptions
        {
            ProjectScope = project,
            DatabasePath = dbPath,
            InstallClaudeSkill = installEverything || claudeSkill,
            InstallCursorSkill = installEverything || cursorSkill,
            InstallCursorRule = installEverything || cursorSkill,
            InstallCursorMcp = installEverything || cursorMcp,
            InstallCopilotMcp = installEverything || copilotMcp,
            InstallOpenCodeMcp = installEverything || openCodeMcp
        };

        var installer = new AgentInstaller();
        var result = installer.Install(options);
        foreach (var message in result.Messages)
        {
            Console.WriteLine(message);
        }

        Console.WriteLine();
        Console.WriteLine("Restart your AI client to pick up MCP changes.");
        return Task.CompletedTask;
    }

    private static async Task InitAsync(string? solution, string? databasePath, bool global, bool noHook)
    {
        var setup = new AgentSetupService();
        var result = await setup.InitializeAsync(new SetupOptions
        {
            SolutionPath = solution,
            DatabasePath = databasePath,
            GlobalAgents = global,
            InstallGitHook = !noHook
        }).ConfigureAwait(false);

        foreach (var message in result.Messages)
        {
            Console.WriteLine(message);
        }

        Console.WriteLine();
        Console.WriteLine("You can now ask your agent: \"How does <symbol> work?\"");
        Console.WriteLine("Restart Cursor to pick up MCP and rule changes.");
    }

    private static async Task EnsureGraphAsync(string? projectRoot)
    {
        var setup = new AgentSetupService();
        Console.WriteLine(await setup.EnsureGraphAsync(projectRoot).ConfigureAwait(false));
    }

    private static async Task StatusAsync(string? projectRoot)
    {
        projectRoot = GraphifyEnvironment.ResolveProjectRoot(projectRoot);
        projectRoot = GraphWorkspace.DiscoverProjectRoot(projectRoot) ?? projectRoot;
        var status = await GraphWorkspace.GetStatusAsync(projectRoot).ConfigureAwait(false);
        Console.WriteLine($"Project root: {projectRoot}");
        Console.WriteLine($"Configured: {status.ConfigExists}");
        Console.WriteLine($"Database: {status.DatabasePath}");
        Console.WriteLine($"Database exists: {status.DatabaseExists}");
        Console.WriteLine($"Stale: {status.IsStale}");
        Console.WriteLine($"Solution: {status.SolutionPath ?? "not found"}");
        Console.WriteLine($"Built at: {status.BuiltAt?.ToString("O") ?? "unknown"}");
        Console.WriteLine($"Nodes: {status.NodeCount:N0}");
        Console.WriteLine($"Edges: {status.EdgeCount:N0}");
    }

    private static async Task HowAsync(string dbPath, string topic)
    {
        var projectRoot = GraphWorkspace.DiscoverProjectRoot() ?? Directory.GetCurrentDirectory();
        var setup = new AgentSetupService();
        await setup.EnsureGraphAsync(projectRoot).ConfigureAwait(false);

        var config = GraphWorkspace.LoadConfig(projectRoot);
        var resolvedDb = config?.DatabasePath ?? dbPath;
        await using var database = await GraphDatabase.OpenAsync(resolvedDb).ConfigureAwait(false);
        var service = new HowDoesItWorkService();
        Console.WriteLine(await service.ExplainAsync(database, topic).ConfigureAwait(false));
    }

    private static async Task InvestigateAsync(string? projectRoot, bool noHandoff, string question)
    {
        projectRoot = GraphifyEnvironment.ResolveProjectRoot(projectRoot);
        projectRoot = GraphWorkspace.DiscoverProjectRoot(projectRoot) ?? projectRoot;

        var setup = new AgentSetupService();
        await setup.EnsureGraphAsync(projectRoot).ConfigureAwait(false);

        var config = GraphWorkspace.LoadConfig(projectRoot);
        var databasePath = config?.DatabasePath ?? GraphWorkspace.GetDefaultDatabasePath(projectRoot);
        await using var database = await GraphDatabase.OpenAsync(databasePath).ConfigureAwait(false);

        var service = new InvestigationService();
        var result = await service.InvestigateAsync(database, question, projectRoot, writeHandoff: !noHandoff).ConfigureAwait(false);
        Console.WriteLine(result.Markdown);
        if (!string.IsNullOrWhiteSpace(result.HandoffPath))
        {
            Console.WriteLine();
            Console.WriteLine($"Handoff: {result.HandoffPath}");
        }
    }

    private static async Task AssessAsync(string dbPath, string topic)
    {
        await using var database = await GraphDatabase.OpenAsync(dbPath).ConfigureAwait(false);
        var assessment = new ArchitectureAssessmentService();

        if (assessment.LooksLikeMediatorQuestion(topic))
        {
            var result = await assessment.AssessMediatorReplacementAsync(database).ConfigureAwait(false);
            Console.WriteLine(result.Markdown);
            return;
        }

        var matches = await database.SearchNodesAsync(topic, limit: 1, justMyCode: true).ConfigureAwait(false);
        if (matches.Count == 0)
        {
            Console.WriteLine($"No symbol matched '{topic}'. Try a type or interface name, or 'mediator' for a MediatR swap assessment.");
            return;
        }

        var removal = await assessment.AssessSymbolRemovalAsync(database, matches[0]).ConfigureAwait(false);
        Console.WriteLine(removal.Markdown);
    }

    private static async Task TraceTableAsync(string? projectRoot, string tableName)
    {
        projectRoot = GraphifyEnvironment.ResolveProjectRoot(projectRoot);
        projectRoot = GraphWorkspace.DiscoverProjectRoot(projectRoot) ?? projectRoot;

        var setup = new AgentSetupService();
        await setup.EnsureGraphAsync(projectRoot).ConfigureAwait(false);

        var config = GraphWorkspace.LoadConfig(projectRoot);
        var databasePath = config?.DatabasePath ?? GraphWorkspace.GetDefaultDatabasePath(projectRoot);
        await using var database = await GraphDatabase.OpenAsync(databasePath).ConfigureAwait(false);

        var service = new TableMigrationTraceService();
        var result = await service.TraceAsync(database, tableName).ConfigureAwait(false);
        Console.WriteLine(result.Markdown);
    }

    private static async Task FlowsAsync(string dbPath, string endpoint)
    {
        await using var database = await GraphDatabase.OpenAsync(dbPath).ConfigureAwait(false);
        var service = new GraphFlowService();
        var paths = await service.FindEndpointFlowsAsync(database, endpoint).ConfigureAwait(false);

        if (paths.Count == 0)
        {
            Console.WriteLine("No endpoint flow found.");
            return;
        }

        var index = 1;
        foreach (var path in paths)
        {
            Console.WriteLine($"Flow {index++}:");
            foreach (var step in path.Steps)
            {
                if (step.IncomingEdge is null)
                {
                    Console.WriteLine($"  {step.Node.FullName ?? step.Node.Name}");
                    continue;
                }

                Console.WriteLine($"  --[{step.IncomingEdge.Relation}/{step.IncomingEdge.Confidence}]--> {step.Node.FullName ?? step.Node.Name}");
            }

            Console.WriteLine();
        }
    }
}
