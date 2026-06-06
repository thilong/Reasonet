using Reasonet.Agent;
using Reasonet.Configuration;
using Reasonet.Events;
using Reasonet.Providers;
using Reasonet.Providers.OpenAI;
using Reasonet.Session;
using Reasonet.Skill;
using Reasonet.Tools;
using Reasonet.Tools.BuiltIn;

namespace Reasonet.App;

/// <summary>
/// Bridges the Reasonet backend with the Avalonia UI.
/// </summary>
public sealed class ReasonetAppController
{
    private readonly MainWindow _window;
    private IRunner? _runner;
    private ISink? _sink;
    private readonly string _workspaceRoot;

    public ReasonetAppController(MainWindow window)
    {
        _window = window;
        _workspaceRoot = Directory.GetCurrentDirectory();
    }

    public async Task InitializeAsync()
    {
        try
        {
            var cfg = ConfigLoader.Load(_workspaceRoot);
            var modelName = cfg.DefaultModel;
            if (string.IsNullOrEmpty(modelName))
            {
                _window.AddSystemMessage("错误: 未配置默认模型");
                return;
            }

            var entry = cfg.Providers.FirstOrDefault(p => p.Name == modelName);
            if (entry == null)
            {
                _window.AddSystemMessage($"错误: 模型 \"{modelName}\" 未找到");
                return;
            }

            var apiKey = Environment.GetEnvironmentVariable(entry.ApiKeyEnv) ?? entry.ApiKeyEnv;
            if (string.IsNullOrEmpty(apiKey))
            {
                _window.AddSystemMessage($"错误: API Key 未设置 (尝试环境变量 {entry.ApiKeyEnv})");
                return;
            }

            var modelId = entry.Model ?? entry.Models.FirstOrDefault() ?? modelName;
            var isDeepSeek = entry.BaseUrl.Contains("deepseek.com", StringComparison.OrdinalIgnoreCase);
            var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

            var provider = new OpenAIProvider(
                entry.Name, apiKey, entry.BaseUrl, modelId,
                isDeepSeek, entry.Effort, httpClient);

            // Build tools
            var registry = new ToolRegistry();
            registry.Add(new ReadFileTool());
            registry.Add(new WriteFileTool());
            registry.Add(new EditFileTool());
            registry.Add(new BashTool());
            registry.Add(new GlobTool());
            registry.Add(new GrepTool());
            registry.Add(new MultiEditTool());
            registry.Add(new ListDirTool());
            registry.Add(new WebFetchTool());
            registry.Add(new BashOutputTool());
            registry.Add(new KillShellTool());
            registry.Add(new WaitTool());
            registry.Add(new TodoWriteTool());
            registry.Add(new CompleteStepTool());
            registry.Add(new RememberTool(new Memory.MemoryStore(_workspaceRoot)));
            registry.Add(new ForgetTool(new Memory.MemoryStore(_workspaceRoot)));

            // Skills
            var skillStore = new SkillStore(new SkillOptions { ProjectRoot = _workspaceRoot });
            var skills = skillStore.List();
            var prompt = cfg.Agent.SystemPrompt;
            if (skills.Count > 0)
                prompt = SkillIndex.Apply(prompt, skills);
            registry.Add(new RunSkillTool(skillStore));
            registry.Add(new InstallSkillTool(skillStore));

            // Memory
            var memorySet = Memory.MemorySet.Load(_workspaceRoot);
            prompt = Memory.MemorySet.Compose(prompt, memorySet);

            // Wire event sink to UI
            _sink = new UiSink(_window);

            var session = new AgentSession(prompt);
            var opts = new AgentOptions
            {
                MaxSteps = cfg.Agent.MaxSteps,
                Temperature = cfg.Agent.Temperature,
                Pricing = entry.Pricing,
                ContextWindow = entry.ContextWindow,
                SoftCompactRatio = cfg.Agent.SoftCompactRatio,
                CompactRatio = cfg.Agent.CompactRatio,
                CompactForceRatio = cfg.Agent.CompactForceRatio,
            };

            var agent = new Reasonet.Agent.Agent(provider, registry, session, opts, _sink);
            registry.Add(new TaskTool(agent));
            _runner = agent;

            _window.AddSystemMessage($"已连接: {entry.Name}/{modelId}");
        }
        catch (Exception ex)
        {
            _window.AddSystemMessage($"初始化失败: {ex.Message}", Avalonia.Media.Brushes.Red);
        }
    }

    public async Task RunAsync(string input)
    {
        if (_runner == null)
        {
            _window.AddSystemMessage("请等待初始化完成");
            return;
        }
        await _runner.RunAsync(input);
    }
}

/// <summary>
/// Event sink that routes agent events to the Avalonia UI.
/// </summary>
public sealed class UiSink : ISink
{
    private readonly MainWindow _window;
    private bool _wroteHeader;
    private bool _hasPendingText;

    public UiSink(MainWindow window) => _window = window;

    public void Emit(Event evt)
    {
        switch (evt.Kind)
        {
            case EventKind.TurnStarted:
                _wroteHeader = false;
                _hasPendingText = false;
                break;

            case EventKind.Reasoning:
                if (!_wroteHeader)
                {
                    _window.AddSystemMessage("  thinking...", Avalonia.Media.Brushes.Gray);
                    _wroteHeader = true;
                }
                break;

            case EventKind.Text:
                if (evt.Text != null)
                {
                    _hasPendingText = true;
                }
                break;

            case EventKind.Message:
                if (evt.Text != null && evt.Text.Length > 0)
                    _window.AddAssistantMessage(evt.Text);
                _wroteHeader = false;
                _hasPendingText = false;
                break;

            case EventKind.ToolDispatch:
                if (evt.Tool != null && !evt.Tool.IsPartial)
                    _window.AddToolMessage($"  -> {evt.Tool.Name}");
                break;

            case EventKind.ToolResult:
                if (evt.Tool != null && !string.IsNullOrEmpty(evt.Tool.Error))
                    _window.AddErrorMessage($"  ! {evt.Tool.Name}: {evt.Tool.Error}");
                break;

            case EventKind.Usage:
                if (evt.Usage != null)
                    _window.AddSystemMessage($"  tokens: ↑{evt.Usage.PromptTokens} ↓{evt.Usage.CompletionTokens} cache:{evt.Usage.CacheHitTokens}/{evt.Usage.CacheHitTokens + evt.Usage.CacheMissTokens}");
                break;

            case EventKind.Notice:
                _window.AddSystemMessage($"  {evt.Text}");
                break;

            case EventKind.CompactionStarted:
                _window.AddSystemMessage("  compacting...");
                break;
        }
    }
}
