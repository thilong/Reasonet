using Reasonet.Events;
using Reasonet.Providers;

namespace Reasonet.Agent;

/// <summary>
/// Renders a turn's event stream as ANSI text to a TextWriter.
/// </summary>
public sealed class TextSink : ISink
{
    private readonly TextWriter _writer;
    private bool _wroteReasoningHeader;
    private bool _wroteReasoningBody;
    private bool _textWritten;
    private bool _showReasoning;
    private bool _wroteAnything;

    public TextSink(TextWriter writer) => _writer = writer;

    public bool ShowReasoning { get => _showReasoning; set => _showReasoning = value; }

    public void Emit(Event evt)
    {
        switch (evt.Kind)
        {
            case EventKind.TurnStarted:
                _wroteReasoningHeader = false;
                _wroteReasoningBody = false;
                _textWritten = false;
                _wroteAnything = false;
                break;

            case EventKind.Reasoning:
                if (!_wroteReasoningHeader)
                {
                    _writer.WriteLine("  \u2593 thinking");
                    _wroteReasoningHeader = true;
                }
                if (_showReasoning && !string.IsNullOrEmpty(evt.Text))
                {
                    _writer.Write(Dim(evt.Text));
                    _wroteReasoningBody = true;
                }
                _wroteAnything = true;
                break;

            case EventKind.Text:
                if (_wroteReasoningHeader && _wroteReasoningBody && !_textWritten)
                    _writer.WriteLine();
                _writer.Write(evt.Text);
                _textWritten = true;
                _wroteAnything = true;
                break;

            case EventKind.Message:
                // Close text stream
                if (_textWritten)
                    _writer.WriteLine();
                _wroteReasoningHeader = false;
                _wroteReasoningBody = false;
                _textWritten = false;
                break;

            case EventKind.ToolDispatch:
                if (evt.Tool is { IsPartial: false } && evt.Tool.Name != null)
                {
                    _writer.WriteLine($"  -> {evt.Tool.Name} {CompactArgs(evt.Tool.Args ?? "")}");
                    _wroteAnything = true;
                }
                break;

            case EventKind.ToolResult:
                if (!string.IsNullOrEmpty(evt.Tool?.Error))
                {
                    _writer.WriteLine($"  \u2298 {evt.Tool.Name} {evt.Tool.Error}");
                    _wroteAnything = true;
                }
                break;

            case EventKind.Usage:
                if (_textWritten)
                {
                    _writer.WriteLine();
                    _textWritten = false;
                }
                PrintUsageLine(evt.Usage, evt.Pricing);
                break;

            case EventKind.Notice:
                var glyph = evt.NoticeLevel == Events.NoticeLevel.Warn ? "!" : "\u00b7";
                _writer.WriteLine($"  {glyph} {evt.Text}");
                _wroteAnything = true;
                break;

            case EventKind.Phase:
                if (_wroteAnything)
                    _writer.WriteLine();
                _writer.WriteLine($"[{evt.Phase}]");
                _wroteAnything = true;
                break;

            case EventKind.CompactionStarted:
                _writer.WriteLine(Dim("  \u22ef compacting conversation\u2026"));
                _wroteAnything = true;
                break;

            case EventKind.CompactionDone:
                var c = evt.Compaction;
                if (c != null && !string.IsNullOrEmpty(c.Summary))
                {
                    _writer.WriteLine(Dim($"  \u22ef compacted {c.Messages} messages ({c.Trigger})"));
                    foreach (var ln in c.Summary.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        _writer.WriteLine(Dim($"    {ln}"));
                    _wroteAnything = true;
                }
                break;

            case EventKind.TurnDone:
                _writer.WriteLine();
                break;
        }
    }

    private void PrintUsageLine(Usage? usage, Pricing? pricing)
    {
        if (usage == null) return;
        var parts = new List<string>
        {
            $"\u2191{usage.PromptTokens} \u2193{usage.CompletionTokens}"
        };
        if (usage.CacheHitTokens > 0 || usage.CacheMissTokens > 0)
        {
            var total = usage.CacheHitTokens + usage.CacheMissTokens;
            var pct = total > 0 ? (int)(usage.CacheHitTokens * 100.0 / total) : 0;
            parts.Add($"cache {pct}%");
        }
        if (pricing != null)
        {
            var cost = pricing.Cost(usage);
            if (cost > 0)
                parts.Add($"{pricing.Currency}{cost:F4}");
        }
        _writer.WriteLine($"  {string.Join(" | ", parts)}");
    }

    private static string Dim(string s) => $"\u001b[2m{s}\u001b[22m";
    private static string Bold(string s) => $"\u001b[1m{s}\u001b[22m";

    private static string CompactArgs(string args)
    {
        if (string.IsNullOrEmpty(args) || args == "{}") return "";
        // Truncate long args
        return args.Length > 60 ? args[..57] + "..." : args;
    }
}
