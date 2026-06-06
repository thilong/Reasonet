using Reasonet.Agent;
using Reasonet.Configuration;
using Reasonet.Events;
using Reasonet.Memory;
using Reasonet.Providers;
using Reasonet.Providers.OpenAI;
using Reasonet.Session;
using Reasonet.Skill;
using Reasonet.Tools;
using Reasonet.Tools.BuiltIn;

var version = "0.3.0-dev";
return await RunAsync(args, version);

async Task<int> RunAsync(string[] args, string version)
{
    if (args.Length == 0)
    {
        PrintWelcome(version);
        return 0;
    }

    var cmd = args[0];
    var rest = args[1..];

    switch (cmd)
    {
        case "run":
            return await RunAgentAsync(rest);
        case "chat":
        case "code":
            return await ChatReplAsync(rest);
        case "history":
            return ListSessionsCommand();
        case "setup":
            return SetupConfig(rest);
        case "config":
            return ConfigCommand(rest);
        case "version":
        case "--version":
        case "-v":
            Console.WriteLine($"reasonet {version}");
            return 0;
        case "help":
        case "--help":
        case "-h":
            PrintUsage();
            return 0;
        default:
            Console.Error.WriteLine($"Unknown command: {cmd}");
            PrintUsage();
            return 2;
    }
}

/// <summary>
/// Resolve workspace root: --dir overrides, else current directory.
/// If --dir is given, change to it so relative paths work.
/// Returns the absolute workspace root path.
/// </summary>
string ResolveWorkspaceRoot(string[] args, out string[] rest)
{
    var filtered = new List<string>();
    string? dir = null;
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--dir" && i + 1 < args.Length)
        {
            dir = args[++i];
        }
        else
        {
            filtered.Add(args[i]);
        }
    }
    rest = filtered.ToArray();

    if (!string.IsNullOrEmpty(dir))
    {
        Directory.SetCurrentDirectory(dir);
    }
    return Path.GetFullPath(".");
}

async Task<int> RunAgentAsync(string[] args)
{
    string? model = null;
    int maxSteps = 0;
    var workspaceRoot = ResolveWorkspaceRoot(args, out var filtered);
    args = filtered;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--model" when i + 1 < args.Length: model = args[++i]; break;
            case "--max-steps" when i + 1 < args.Length: maxSteps = int.Parse(args[++i]); break;
        }
    }

    var prompt = string.Join(" ", args.Where(a => !a.StartsWith("--"))).Trim();
    if (string.IsNullOrEmpty(prompt))
        prompt = await Console.In.ReadToEndAsync();

    if (string.IsNullOrEmpty(prompt))
    {
        Console.Error.WriteLine("Usage: reasonet run [--model <name>] [--dir <path>] <prompt>");
        return 2;
    }

    var (ctrl, err) = await BuildControllerAsync(model, maxSteps, workspaceRoot);
    if (err != null)
    {
        Console.Error.WriteLine($"error: {err}");
        return 1;
    }

    try
    {
        await ctrl!.RunAsync(prompt);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"\nerror: {ex.Message}");
        return 1;
    }
}

async Task<int> ChatReplAsync(string[] args)
{
    string? model = null;
    int maxSteps = 0;
    bool resumeFlag = false;
    bool continueFlag = false;
    var workspaceRoot = ResolveWorkspaceRoot(args, out var filtered);
    args = filtered;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--model" when i + 1 < args.Length: model = args[++i]; break;
            case "--max-steps" when i + 1 < args.Length: maxSteps = int.Parse(args[++i]); break;
            case "--resume": resumeFlag = true; break;
            case "--continue":
            case "-c":
                continueFlag = true;
                break;
        }
    }

    var (ctrl, err) = await BuildControllerAsync(model, maxSteps, workspaceRoot);
    if (err != null)
    {
        Console.Error.WriteLine($"error: {err}");
        return 1;
    }

    // All data lives under <workspace>/.reasonet/
    var sessionDir = SessionManager.SessionDir(workspaceRoot);

    // Handle --resume: pick from list
    if (resumeFlag)
    {
        var sessions = SessionManager.ListSessions(sessionDir);
        if (sessions.Count == 0)
        {
            Console.Error.WriteLine("No saved sessions to resume.");
            return 1;
        }

        Console.WriteLine("\n  Saved sessions:");
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            var when = s.LastActivityAt.ToLocalTime().ToString("MM-dd HH:mm");
            var preview = string.IsNullOrEmpty(s.Preview) ? "(no messages)" : s.Preview;
            Console.WriteLine($"  [{i + 1}] {when}  {s.Turns,3} turns  \u00b7 {s.Model,-24}  {preview}");
        }

        Console.Write("\n  Select session to resume (1-{0}): ", sessions.Count);
        var input = Console.ReadLine();
        if (int.TryParse(input, out var idx) && idx >= 1 && idx <= sessions.Count)
        {
            var selected = sessions[idx - 1];
            var loaded = await AgentSession.LoadAsync(selected.Path);
            ctrl.SetSession(loaded);
            ctrl.SetSessionPath(selected.Path);
            Console.WriteLine($"  \u00b7 resumed: {selected.FileName}");
        }
        else
        {
            Console.Error.WriteLine("Invalid selection.");
            return 1;
        }
    }
    // Handle --continue: resume most recent
    else if (continueFlag)
    {
        var sessions = SessionManager.ListSessions(sessionDir);
        if (sessions.Count == 0)
        {
            Console.Error.WriteLine("No saved sessions to continue.");
            return 1;
        }
        var latest = sessions[0];
        var loaded = await AgentSession.LoadAsync(latest.Path);
        ctrl.SetSession(loaded);
        ctrl.SetSessionPath(latest.Path);
        Console.WriteLine($"  \u00b7 continuing: {latest.FileName}");
    }
    // Fresh session: mint a new path
    else
    {
        var freshPath = SessionManager.NewSessionPath(sessionDir, ctrl.ModelName);
        ctrl.SetSessionPath(freshPath);
    }

    Console.WriteLine($"\n  \u25c6 Reasonet Chat  ({workspaceRoot})");
    Console.WriteLine("  Commands: /exit  /new  /compact  /history  /resume  # note");

    while (true)
    {
        Console.Write("  \u001b[1m>\u001b[22m ");
        var rawInput = Console.ReadLine();
        if (rawInput == null) break;

        var trimmed = rawInput.Trim();
        if (trimmed is "/exit" or "/quit") break;
        if (string.IsNullOrEmpty(trimmed)) continue;

        switch (trimmed)
        {
            case "/new":
                await ctrl.Session.SaveAsync(ctrl.SessionPath);
                var newPath = SessionManager.NewSessionPath(sessionDir, ctrl.ModelName);
                ctrl.SetSession(new AgentSession(ctrl.SystemPrompt));
                ctrl.SetSessionPath(newPath);
                Console.WriteLine("  \u00b7 new session started");
                continue;

            case "/compact":
                await ctrl.CompactAsync("manual");
                Console.WriteLine("  \u00b7 compacted");
                continue;

            case "/history":
                ListSessionsCommand(sessionDir);
                continue;

            case "/resume":
                var sessions = SessionManager.ListSessions(sessionDir);
                if (sessions.Count == 0)
                {
                    Console.WriteLine("  \u00b7 no saved sessions");
                    continue;
                }
                Console.WriteLine();
                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    var when = s.LastActivityAt.ToLocalTime().ToString("MM-dd HH:mm");
                    var preview = string.IsNullOrEmpty(s.Preview) ? "(no messages)" : s.Preview;
                    Console.WriteLine($"  [{i + 1}] {when}  {s.Turns,3} turns  \u00b7 {s.Model,-24}  {preview}");
                }
                Console.Write("\n  Select session to resume (1-{0}): ", sessions.Count);
                var sel = Console.ReadLine();
                if (int.TryParse(sel, out var idx2) && idx2 >= 1 && idx2 <= sessions.Count)
                {
                    var selected = sessions[idx2 - 1];
                    var loaded = await AgentSession.LoadAsync(selected.Path);
                    ctrl.SetSession(loaded);
                    ctrl.SetSessionPath(selected.Path);
                    Console.WriteLine($"  \u00b7 resumed: {selected.FileName}");
                }
                continue;

            default:
                // Quick-add memory with # prefix
                if (trimmed.StartsWith("#"))
                {
                    var note = trimmed[1..].Trim();
                    if (!string.IsNullOrEmpty(note) && ctrl.MemorySet != null)
                    {
                        var path = ctrl.MemorySet.AppendNote(note);
                        Console.WriteLine($"  \u00b7 remembered \u2192 {path}");
                    }
                    continue;
                }
                if (trimmed.StartsWith("/"))
                {
                    Console.WriteLine($"  \u00b7 unknown command: {trimmed}");
                    continue;
                }
                break;
        }

        try
        {
            await ctrl.RunAsync(trimmed);
            _ = ctrl.Session.SaveAsync(ctrl.SessionPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\nerror: {ex.Message}");
        }
    }

    await ctrl.Session.SaveAsync(ctrl.SessionPath);
    return 0;
}

int ListSessionsCommand(string? sessionDir = null)
{
    var dir = sessionDir ?? SessionManager.SessionDir();
    var sessions = SessionManager.ListSessions(dir);
    if (sessions.Count == 0)
    {
        Console.WriteLine("No saved sessions.");
        return 0;
    }

    Console.WriteLine($"\n  Saved sessions ({sessions.Count} total):");
    Console.WriteLine();
    foreach (var s in sessions)
    {
        var when = s.LastActivityAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var preview = string.IsNullOrEmpty(s.Preview) ? "(no messages)" : s.Preview;
        Console.WriteLine($"  {when}  {s.Turns,3} turns  {s.Model,-24}  {preview}");
    }
    Console.WriteLine();
    return 0;
}

int SetupConfig(string[] args)
{
    Console.WriteLine("""
        
          \u25c6 Reasonet Setup
        
        Create a configuration file at <project>/.reasonet/config.toml.
        See the example in the project docs.
        
        """);
    return 0;
}

int ConfigCommand(string[] args)
{
    var workspaceRoot = Path.GetFullPath(".");
    var cfg = ConfigLoader.Load(workspaceRoot);
    Console.WriteLine($"Current configuration ({workspaceRoot}/.reasonet/):");
    Console.WriteLine($"  Default model: {cfg.DefaultModel}");
    Console.WriteLine($"  Language: {cfg.Language}");
    Console.WriteLine($"  Providers: {cfg.Providers.Count}");
    foreach (var p in cfg.Providers)
        Console.WriteLine($"    - {p.Name} ({p.Kind}, {p.Model ?? string.Join(", ", p.Models)})");
    return 0;
}

void PrintWelcome(string version)
{
    Console.WriteLine($$"""
        
          \u25c6 Reasonet v{{version}}
          A config- and plugin-driven coding agent.
        
        Usage: reasonet <command> [options]
        
        Commands:
          run               Run a one-shot agent task
          chat              Start an interactive session
          chat --dir <path> Start chat in a specific workspace directory
          chat --resume     Pick a saved session to resume
          chat --continue   Resume the most recent session
          history           List saved sessions
          setup             Run the configuration wizard
          config            Show current configuration
          help              Show this help message
          version           Show version
        
        """);
}

void PrintUsage()
{
    Console.WriteLine("""
        
        Usage: reasonet <command> [options]
        
        Commands:
          run               Run a one-shot agent task
          chat              Start an interactive session (uses cwd as workspace)
          chat --dir <path> Start chat in a specific workspace directory
          chat --resume     Pick a saved session to resume
          chat --continue   Resume the most recent session
          history           List saved sessions
          setup             Run the configuration wizard
          config            Show current configuration
          help              Show this help message
          version           Show version
        
        """);
}
{
    Console.WriteLine("""
        
        Usage: reasonet <command> [options]
        
        Commands:
          run               Run a one-shot agent task
          chat              Start an interactive session (uses cwd as workspace)
          chat --dir <path> Start chat in a specific workspace directory
          chat --resume     Pick a saved session to resume
          chat --continue   Resume the most recent session
          history           List saved sessions
          setup             Run the configuration wizard
          config            Show current configuration
          help              Show this help message
          version           Show version
        
        """);
}

async Task<(ChatController? Controller, string? Error)> BuildControllerAsync(
    string? modelName, int maxStepsOverride, string workspaceRoot, ISink? customSink = null)
{
    try
    {
        var cfg = ConfigLoader.Load(workspaceRoot);
        modelName ??= cfg.DefaultModel;
        if (string.IsNullOrEmpty(modelName))
            return (null, "no default model configured; create .reasonet/config.toml");

        var entry = cfg.Providers.FirstOrDefault(p => p.Name == modelName);
        if (entry == null)
            return (null, $"model \"{modelName}\" not found in providers");

        var apiKey = Environment.GetEnvironmentVariable(entry.ApiKeyEnv);
        if (string.IsNullOrEmpty(apiKey))
            apiKey = entry.ApiKeyEnv;

        if (string.IsNullOrEmpty(apiKey))
            return (null, $"API key not found: tried env \"{entry.ApiKeyEnv}\" and direct config value");

        var modelId = entry.Model ?? entry.Models.FirstOrDefault() ?? modelName;
        var isDeepSeek = entry.BaseUrl.Contains("deepseek.com", StringComparison.OrdinalIgnoreCase);
        var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        var provider = new OpenAIProvider(
            entry.Name, apiKey, entry.BaseUrl, modelId,
            isDeepSeek, entry.Effort, httpClient);

        var registry = new ToolRegistry();
        registry.Add(new ReadFileTool());
        registry.Add(new WriteFileTool());
        registry.Add(new EditFileTool());
        registry.Add(new BashTool());
        registry.Add(new GlobTool());
        registry.Add(new GrepTool());

        // Load memory and compose into the system prompt
        var memorySet = MemorySet.Load(workspaceRoot);

        // Load skills and apply skills index to the system prompt
        var skillStore = new SkillStore(new SkillOptions
        {
            ProjectRoot = workspaceRoot,
            DisabledNames = cfg.Skills?.DisabledSkills ?? new List<string>(),
        });
        var skills = skillStore.List();
        var withSkills = skills.Count > 0
            ? SkillIndex.Apply(cfg.Agent.SystemPrompt, skills)
            : cfg.Agent.SystemPrompt;
        var basePrompt = MemorySet.Compose(withSkills, memorySet);

        // Register skill tools
        registry.Add(new RunSkillTool(skillStore));
        registry.Add(new InstallSkillTool(skillStore));

        // Register memory tools
        registry.Add(new RememberTool(memorySet.Store));
        registry.Add(new ForgetTool(memorySet.Store));

        // Register planning and editing tools
        registry.Add(new TodoWriteTool());
        registry.Add(new CompleteStepTool());
        registry.Add(new MultiEditTool());

        // Register utility tools
        registry.Add(new ListDirTool());
        registry.Add(new WebFetchTool());
        registry.Add(new BashOutputTool());
        registry.Add(new KillShellTool());
        registry.Add(new WaitTool());

        var session = new AgentSession(basePrompt);
        var sink = customSink ?? new TextSink(Console.Out);

        var opts = new AgentOptions
        {
            MaxSteps = maxStepsOverride > 0 ? maxStepsOverride : cfg.Agent.MaxSteps,
            Temperature = cfg.Agent.Temperature,
            Pricing = entry.Pricing,
            ContextWindow = entry.ContextWindow,
            SoftCompactRatio = cfg.Agent.SoftCompactRatio,
            CompactRatio = cfg.Agent.CompactRatio,
            CompactForceRatio = cfg.Agent.CompactForceRatio,
        };

        IRunner runner;
        Agent agent;

        if (!string.IsNullOrEmpty(cfg.Agent.PlannerModel))
        {
            var plannerEntry = cfg.Providers.FirstOrDefault(p => p.Name == cfg.Agent.PlannerModel);
            if (plannerEntry == null)
                return (null, $"planner model \"{cfg.Agent.PlannerModel}\" not found");

            var plannerKey = Environment.GetEnvironmentVariable(plannerEntry.ApiKeyEnv);
            if (string.IsNullOrEmpty(plannerKey))
                plannerKey = plannerEntry.ApiKeyEnv;
            if (string.IsNullOrEmpty(plannerKey))
                return (null, $"planner API key not found");

            var plannerMid = plannerEntry.Model ?? plannerEntry.Models.FirstOrDefault() ?? cfg.Agent.PlannerModel;
            var plannerDS = plannerEntry.BaseUrl.Contains("deepseek.com", StringComparison.OrdinalIgnoreCase);
            var plannerHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

            var plannerProv = new OpenAIProvider(
                plannerEntry.Name, plannerKey, plannerEntry.BaseUrl,
                plannerMid, plannerDS, plannerEntry.Effort, plannerHttp);

            agent = new Agent(provider, registry, session, opts, sink);
            var plannerSession = new AgentSession(
                "You are the planner in a two-model coding agent. " +
                "Produce concise, ordered plans. Do not write implementations.");
            runner = new Coordinator(plannerProv, plannerSession, plannerEntry.Pricing,
                agent, opts.Temperature, sink);
        }
        else
        {
            agent = new Agent(provider, registry, session, opts, sink);
            runner = agent;
        }

        // Register task tool (needs the agent as ISubAgentRunner, so register late)
        registry.Add(new TaskTool(agent));

        var label = entry.Name + "/" + modelId;
        return (new ChatController(runner, agent, session, cfg.Agent.SystemPrompt, label, memorySet), null);
    }
    catch (Exception ex)
    {
        return (null, ex.Message);
    }
}

public sealed class ChatController
{
    private readonly IRunner _runner;
    private readonly Agent _agent;
    private string _sessionPath;

    public ChatController(IRunner runner, Agent agent, AgentSession session,
        string systemPrompt, string modelName, MemorySet? memorySet = null)
    {
        _runner = runner;
        _agent = agent;
        Session = session;
        SystemPrompt = systemPrompt;
        ModelName = modelName;
        _sessionPath = "";
        MemorySet = memorySet;
    }

    public MemorySet? MemorySet { get; }

    public AgentSession Session { get; private set; }
    public string SystemPrompt { get; }
    public string ModelName { get; }
    public string SessionPath => _sessionPath;

    public void SetSession(AgentSession session)
    {
        Session = session;
        _agent.SetSession(session);
    }

    public void SetSessionPath(string path)
    {
        _sessionPath = path;
    }

    public async Task RunAsync(string input)
    {
        await _runner.RunAsync(input);
    }

    public async Task CompactAsync(string trigger)
    {
        await _agent.CompactAsync(trigger);
    }
}
