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

public sealed class ReasonetAppController
{
    private readonly MainWindow _w;
    private IRunner? _runner;
    private readonly string _root;

    public ReasonetAppController(MainWindow w) { _w = w; _root = Directory.GetCurrentDirectory(); }

    public async Task InitializeAsync()
    {
        try
        {
            var cfg = ConfigLoader.Load(_root);
            var mn = cfg.DefaultModel;
            if (string.IsNullOrEmpty(mn)) { _w.AddInline("错误: 未配置默认模型"); return; }
            var entry = cfg.Providers.FirstOrDefault(p => p.Name == mn);
            if (entry == null) { _w.AddInline($"错误: 模型 \"{mn}\" 未找到"); return; }

            var key = Environment.GetEnvironmentVariable(entry.ApiKeyEnv) ?? entry.ApiKeyEnv;
            if (string.IsNullOrEmpty(key)) { _w.AddInline($"错误: API Key 未设置 ({entry.ApiKeyEnv})"); return; }

            var mid = entry.Model ?? entry.Models.FirstOrDefault() ?? mn;
            var ds = entry.BaseUrl.Contains("deepseek.com", StringComparison.OrdinalIgnoreCase);
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var prov = new OpenAIProvider(entry.Name, key, entry.BaseUrl, mid, ds, entry.Effort, http);

            var reg = new ToolRegistry();
            reg.Add(new ReadFileTool()); reg.Add(new WriteFileTool()); reg.Add(new EditFileTool());
            reg.Add(new BashTool()); reg.Add(new GlobTool()); reg.Add(new GrepTool());
            reg.Add(new MultiEditTool()); reg.Add(new ListDirTool()); reg.Add(new WebFetchTool());
            reg.Add(new BashOutputTool()); reg.Add(new KillShellTool()); reg.Add(new WaitTool());
            reg.Add(new TodoWriteTool()); reg.Add(new CompleteStepTool());
            reg.Add(new RememberTool(new Memory.MemoryStore(_root)));
            reg.Add(new ForgetTool(new Memory.MemoryStore(_root)));

            var sk = new SkillStore(new SkillOptions { ProjectRoot = _root });
            var skills = sk.List();
            var prompt = cfg.Agent.SystemPrompt;
            if (skills.Count > 0) prompt = SkillIndex.Apply(prompt, skills);
            reg.Add(new RunSkillTool(sk)); reg.Add(new InstallSkillTool(sk));
            prompt = Memory.MemorySet.Compose(prompt, Memory.MemorySet.Load(_root));

            var sink = new UiSink(_w);
            var session = new AgentSession(prompt);
            var opts = new AgentOptions
            {
                MaxSteps = cfg.Agent.MaxSteps, Temperature = cfg.Agent.Temperature,
                Pricing = entry.Pricing, ContextWindow = entry.ContextWindow,
                SoftCompactRatio = cfg.Agent.SoftCompactRatio, CompactRatio = cfg.Agent.CompactRatio,
                CompactForceRatio = cfg.Agent.CompactForceRatio,
            };
            var agent = new Reasonet.Agent.Agent(prov, reg, session, opts, sink);
            reg.Add(new TaskTool(agent));
            _runner = agent;
            _w.AddInline($"已连接: {entry.Name}/{mid}");
        }
        catch (Exception ex) { _w.AddErrorMessage($"初始化失败: {ex.Message}"); }
    }

    public async Task RunAsync(string i)
    {
        if (_runner == null) { _w.AddInline("等待初始化完成"); return; }
        await _runner.RunAsync(i);
    }
}

public sealed class UiSink : ISink
{
    private readonly MainWindow _w;
    private bool _thinking;
    private string _pausedText = "";
    public UiSink(MainWindow w) => _w = w;

    public void Emit(Event evt)
    {
        switch (evt.Kind)
        {
            case EventKind.TurnStarted:
                _thinking = false;
                _w.Chat.CloseCurrentStream();
                break;

            case EventKind.Reasoning:
                if (!_thinking) { _w.AddInline("  thinking..."); _thinking = true; }
                break;

            case EventKind.Text:
                if (evt.Text?.Length > 0)
                    _w.Chat.AppendToStream(evt.Text);
                break;

            case EventKind.Message:
                _thinking = false;
                if (evt.Text?.Length > 0)
                    _w.Chat.FinalizeMessage(evt.Text);
                else
                    _w.Chat.CloseCurrentStream();
                break;

            case EventKind.ToolDispatch:
                _w.Chat.CloseCurrentStream();
                if (evt.Tool != null && !evt.Tool.IsPartial)
                {
                    var a = evt.Tool.Args ?? "";
                    _w.AddInline($"  ▶ {evt.Tool.Name}" + (a == "{}" || a == "" ? "" : $" {Compact(a)}"), Avalonia.Media.Brushes.DimGray);
                }
                break;

            case EventKind.ToolResult:
                if (evt.Tool != null && !string.IsNullOrEmpty(evt.Tool.Error))
                    _w.AddErrorMessage($"  ✘ {evt.Tool.Name}: {evt.Tool.Error}");
                else if (evt.Tool?.Output?.Length > 0)
                {
                    var lines = evt.Tool.Output.Split('\n');
                    var prev = string.Join('\n', lines.Take(8));
                    if (lines.Length > 8) prev += $"\n  ... ({lines.Length} lines)";
                    _w.AddToolBubble(prev);
                }
                break;

            case EventKind.Usage:
                if (evt.Usage != null)
                    _w.AddInline($"  tokens: ↑{evt.Usage.PromptTokens} ↓{evt.Usage.CompletionTokens}  hit:{evt.Usage.CacheHitTokens}  miss:{evt.Usage.CacheMissTokens}");
                break;

            case EventKind.Notice: _w.AddInline($"  {evt.Text}"); break;
            case EventKind.CompactionStarted: _w.AddInline("  compacting..."); break;
            case EventKind.Phase: _w.AddInline($"[{evt.Phase}]"); break;
        }
    }

    static string Compact(string s) => s.Length > 50 ? s[..47] + "..." : s;
}
