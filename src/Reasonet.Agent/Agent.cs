using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Reasonet.Events;
using Reasonet.Messages;
using Reasonet.Providers;
using Reasonet.Session;
using Reasonet.Tools;

namespace Reasonet.Agent;

/// <summary>
/// Options for configuring an Agent.
/// </summary>
public sealed record AgentOptions
{
    public int MaxSteps { get; init; }
    public double Temperature { get; init; }
    public Pricing? Pricing { get; init; }
    public int ContextWindow { get; init; }
    public double SoftCompactRatio { get; init; } = 0.5;
    public double CompactRatio { get; init; } = 0.8;
    public double CompactForceRatio { get; init; } = 0.9;
    public int RecentKeep { get; init; } = 2;
    public string? ArchiveDir { get; init; }
}

/// <summary>
/// Outcome of a single tool call execution.
/// </summary>
internal sealed record ToolOutcome(
    string Output,
    bool Blocked = false,
    string? ErrorMsg = null,
    bool Truncated = false,
    string? TruncMsg = null
);

/// <summary>
/// Gate decides per tool call whether it may run.
/// </summary>
public interface IGate
{
    (bool Allow, string Reason) Check(string toolName, string argumentsJson, bool isReadOnly);
}

/// <summary>
/// Agent drives a single task: a Provider, a ToolRegistry, and a Session
/// wired into the main loop. Also implements ISubAgentRunner so it can
/// spawn sub-agents for the task tool.
/// </summary>
public sealed class Agent : IRunner, ISubAgentRunner
{
    private const int MaxToolOutputBytes = 32 * 1024;
    private const int MaxFinalReadinessBlocks = 3;

    private readonly IProvider _prov;
    private readonly ToolRegistry _tools;
    private AgentSession _session;
    private readonly AgentOptions _opts;
    private readonly ISink _sink;
    private Usage? _lastUsage;
    private long _sessCacheHit;
    private long _sessCacheMiss;
    private bool _planMode;
    private IGate? _gate;
    private bool _compactStuck;
    private int _consecutiveCompacts;
    private bool _softCompactNoticed;

    // Storm breaker
    private string _stormSig = "";
    private int _stormCount;
    private Dictionary<string, int>? _repeatSuccessCounts;

    public Agent(
        IProvider prov,
        ToolRegistry tools,
        AgentSession session,
        AgentOptions opts,
        ISink sink)
    {
        _prov = prov;
        _tools = tools;
        _session = session;
        _opts = opts;
        _sink = sink;
    }

    public AgentSession Session => _session;
    public void SetSession(AgentSession s) => _session = s;
    public bool PlanMode { get => _planMode; set => _planMode = value; }
    public IGate? Gate { get => _gate; set => _gate = value; }
    public Usage? LastUsage => _lastUsage;
    public (long Hit, long Miss) SessionCache => (_sessCacheHit, _sessCacheMiss);
    public int ContextWindow => _opts.ContextWindow;
    public double CompactRatio => _opts.CompactRatio;

    /// <summary>
    /// Run the main agent loop: user input → LLM calls → tool execution → done.
    /// </summary>
    public async Task<string> RunAsync(string input, CancellationToken ct = default)
    {
        _stormSig = "";
        _stormCount = 0;
        _repeatSuccessCounts = null;

        _sink.Emit(new Event(EventKind.TurnStarted));
        _session.Add(new Message { Role = Role.User, Content = input });

        int finalReadinessBlocks = 0;

        for (int step = 0; _opts.MaxSteps <= 0 || step < _opts.MaxSteps; step++)
        {
            var schemas = _tools.Schemas();

            // Stream from provider
            var (text, reasoning, signature, calls, usage) =
                await StreamAsync(schemas, step + 1, ct);

            if (usage != null)
            {
                _lastUsage = usage;
                _sink.Emit(new Event(EventKind.Usage, Usage: usage, Pricing: _opts.Pricing));
            }

            // Store assistant response
            _session.Add(new Message
            {
                Role = Role.Assistant,
                Content = text,
                ReasoningContent = reasoning,
                ReasoningSignature = signature,
                ToolCalls = calls.Count > 0 ? calls : null
            });

            // No tool calls → final answer
            if (calls.Count == 0)
            {
                // Check final readiness
                // (simplified: no evidence ledger in this implementation)
                return text ?? "";
            }

            // Execute tools
            var results = await ExecuteBatchAsync(calls, ct);

            // Store tool results
            for (int i = 0; i < calls.Count; i++)
            {
                _session.Add(new Message
                {
                    Role = Role.Tool,
                    Content = results[i].Output,
                    ToolCallId = calls[i].Id,
                    Name = calls[i].Name
                });
            }

            // Check compaction
            MaybeCompact(usage);
        }

        _session.Add(new Message
        {
            Role = Role.Assistant,
            Content = "Paused: max steps reached. Send another message to continue."
        });
        return "Paused: max steps reached.";
    }

    /// <summary>
    /// Stream one completion from the provider.
    /// </summary>
    private async Task<(string? Text, string? Reasoning, string? Signature, List<ToolCall> Calls, Usage? Usage)>
        StreamAsync(IReadOnlyList<ToolSchema> schemas, int turn, CancellationToken ct)
    {
        var text = new StringBuilder();
        var reasoning = new StringBuilder();
        var signature = "";
        var calls = new List<ToolCall>();
        Usage? usage = null;

        var request = new ProviderRequest(
            _session.Snapshot(),
            schemas,
            _opts.Temperature
        );

        await foreach (var chunk in _prov.StreamAsync(request, ct))
        {
            switch (chunk.Type)
            {
                case ChunkType.Reasoning:
                    reasoning.Append(chunk.Text);
                    if (chunk.Signature != null) signature = chunk.Signature;
                    if (!string.IsNullOrEmpty(chunk.Text))
                        _sink.Emit(new Event(EventKind.Reasoning, Text: chunk.Text));
                    break;

                case ChunkType.Text:
                    text.Append(chunk.Text);
                    _sink.Emit(new Event(EventKind.Text, Text: chunk.Text));
                    break;

                case ChunkType.ToolCallStart:
                    if (chunk.ToolCall != null)
                    {
                        _sink.Emit(new Event(EventKind.ToolDispatch, Tool: new ToolEvent(
                            Id: chunk.ToolCall.Id ?? "",
                            Name: chunk.ToolCall.Name ?? "",
                            IsPartial: true)));
                    }
                    break;

                case ChunkType.ToolCall:
                    if (chunk.ToolCall != null)
                    {
                        calls.Add(new ToolCall
                        {
                            Id = chunk.ToolCall.Id ?? $"call_{calls.Count}",
                            Name = chunk.ToolCall.Name ?? "unknown",
                            Arguments = chunk.ToolCall.Arguments ?? ""
                        });
                    }
                    break;

                case ChunkType.Usage:
                    usage = chunk.Usage;
                    if (usage != null)
                    {
                        Interlocked.Exchange(ref _lastUsage, usage);
                        Interlocked.Add(ref _sessCacheHit, usage.CacheHitTokens);
                        Interlocked.Add(ref _sessCacheMiss, usage.CacheMissTokens);
                    }
                    break;

                case ChunkType.Error:
                    if (chunk.Error != null)
                        throw chunk.Error;
                    break;
            }
        }

        // Close text stream
        if (text.Length > 0 || reasoning.Length > 0)
        {
            _sink.Emit(new Event(EventKind.Message,
                Text: text.ToString(),
                Reasoning: reasoning.ToString()));
        }

        return (text.ToString(), reasoning.ToString(), signature, calls, usage);
    }

    /// <summary>
    /// Execute a batch of tool calls, parallelizing contiguous read-only tools.
    /// </summary>
    private async Task<List<ToolOutcome>> ExecuteBatchAsync(
        List<ToolCall> calls, CancellationToken ct)
    {
        // Emit full dispatch events
        foreach (var c in calls)
        {
            var t = _tools.Get(c.Name);
            _sink.Emit(new Event(EventKind.ToolDispatch, Tool: new ToolEvent(
                Id: c.Id, Name: c.Name, Args: c.Arguments,
                IsReadOnly: t?.IsReadOnly ?? false)));
        }

        var outcomes = new ToolOutcome[calls.Count];

        // Partition into parallelizable segments
        var batches = PartitionToolCalls(calls);

        foreach (var batch in batches)
        {
            if (batch.Parallel && batch.Count > 1)
            {
                // Parallel execution
                var tasks = new Task<ToolOutcome>[batch.Count];
                for (int i = 0; i < batch.Count; i++)
                {
                    var idx = batch.Start + i;
                    var call = calls[idx];
                    tasks[i] = Task.Run(() => ExecuteOneAsync(call, ct), ct);
                }
                var results = await Task.WhenAll(tasks);
                for (int i = 0; i < results.Length; i++)
                    outcomes[batch.Start + i] = results[i];
            }
            else
            {
                // Sequential execution
                for (int i = batch.Start; i < batch.End; i++)
                    outcomes[i] = await ExecuteOneAsync(calls[i], ct);
            }
        }

        // Emit results
        for (int i = 0; i < calls.Count; i++)
        {
            var o = outcomes[i];
            var c = calls[i];
            _sink.Emit(new Event(EventKind.ToolResult, Tool: new ToolEvent(
                Id: c.Id, Name: c.Name, Args: c.Arguments,
                Output: o.Output, Error: o.ErrorMsg,
                Truncated: o.Truncated)));
        }

        // Storm breaker
        ApplyStormBreaker(calls, outcomes);

        return outcomes.ToList();
    }

    /// <summary>
    /// Execute a single tool call through the full gate chain.
    /// </summary>
    private async Task<ToolOutcome> ExecuteOneAsync(ToolCall call, CancellationToken ct)
    {
        var t = _tools.Get(call.Name);
        if (t == null)
            return new ToolOutcome(
                $"error: unknown tool \"{call.Name}\"",
                ErrorMsg: $"unknown tool \"{call.Name}\"");

        // Plan mode check
        if (_planMode && !t.IsReadOnly)
            return new ToolOutcome(
                $"blocked: \"{call.Name}\" is a writer tool and plan mode is read-only.",
                Blocked: true, ErrorMsg: "blocked: plan mode is read-only");

        // Gate check
        if (_gate != null)
        {
            var (allow, reason) = _gate.Check(call.Name, call.Arguments, t.IsReadOnly);
            if (!allow)
                return new ToolOutcome($"blocked: {reason}", Blocked: true,
                    ErrorMsg: "blocked by permission policy");
        }

        // Repeated success block — bail early when the same write already succeeded
        var (blockMsg, blocked) = RepeatedSuccessBlock(call, t);
        if (blocked)
            return new ToolOutcome(blockMsg, Blocked: true, ErrorMsg: "blocked by loop guard");

        // Execute
        try
        {
            var result = await t.ExecuteAsync(call.Arguments, ct);
            // Record successful write for the repeat-success guard
            RecordRepeatSuccess(call, t);
            var (body, truncMsg) = TruncateToolOutput(result);
            return new ToolOutcome(body, Truncated: truncMsg != null, TruncMsg: truncMsg);
        }
        catch (Exception ex)
        {
            var body = $"error: {ex.Message}\n{ex.StackTrace}";
            var (truncated, _) = TruncateToolOutput(body);
            return new ToolOutcome(truncated, ErrorMsg: ex.Message);
        }
    }

    /// <summary>
    /// Truncate tool output if it exceeds the maximum allowed bytes.
    /// </summary>
    internal static (string Body, string? Notice) TruncateToolOutput(string s)
    {
        if (s.Length <= MaxToolOutputBytes)
            return (s, null);

        var keep = MaxToolOutputBytes / 2;
        var head = s[..keep];
        var tail = s[^keep..];
        var omitted = s.Length - keep * 2;

        var notice = $"tool output truncated: {omitted} of {s.Length} bytes elided";
        var body = $"{head}\n\n...[truncated {omitted} bytes]...\n\n{tail}";
        return (body, notice);
    }

    #region Compaction

    // Compaction constants matching Reasonet
    private const int DefaultTailTokens = 16384;     // verbatim recent-tail budget
    private const int MinRecentKeep = 2;              // never keep fewer messages
    private const int MinCompactMessages = 2;         // skip below this many
    private const int MinFoldTokens = 400;            // skip uneconomic folds
    private const double DefaultCompactTarget = 0.5;  // tail never exceeds 50% of window
    private const double FallbackTokPerChar = 0.25;   // ~4 chars/token before calibration

    private const string SummaryTagOpen = "<compaction-summary>";
    private const string SummaryTagClose = "</compaction-summary>";

    /// <summary>
    /// Check and trigger compaction if the prompt has grown too large.
    /// </summary>
    internal void MaybeCompact(Usage? u)
    {
        if (_opts.ContextWindow <= 0 || u == null || u.PromptTokens == 0)
            return;

        var high = (int)(_opts.ContextWindow * _opts.CompactRatio);
        var soft = (int)(_opts.ContextWindow * _opts.SoftCompactRatio);

        // Between soft ratio and trigger: report once, never compact (keep cache)
        if (u.PromptTokens >= soft && u.PromptTokens < high && !_softCompactNoticed)
        {
            _softCompactNoticed = true;
            _sink.Emit(new Event(EventKind.Notice, NoticeLevel: Events.NoticeLevel.Info,
                Text: $"context reached {_opts.SoftCompactRatio * 100:F0}% of window; " +
                      $"keeping cache-first prefix until compact threshold {_opts.CompactRatio * 100:F0}%"));
            return;
        }

        // Under the trigger: healthy — clear stuck latch and counters
        if (u.PromptTokens < high)
        {
            _consecutiveCompacts = 0;
            _compactStuck = false;
            _softCompactNoticed = false;
            return;
        }

        if (_compactStuck) return;

        var force = u.PromptTokens >= (int)(_opts.ContextWindow * _opts.CompactForceRatio);
        try
        {
            CompactAsync("auto", force).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _sink.Emit(new Event(EventKind.Notice, NoticeLevel: Events.NoticeLevel.Info,
                Text: $"compaction skipped: {ex.Message}"));
            return;
        }

        // If we compacted and still over threshold, we're stuck
        _consecutiveCompacts++;
        if (_consecutiveCompacts >= 2)
        {
            _compactStuck = true;
            _sink.Emit(new Event(EventKind.Notice, NoticeLevel: Events.NoticeLevel.Warn,
                Text: $"context_window={_opts.ContextWindow} is too small for compaction " +
                      $"to help (the system prompt plus one turn already exceeds " +
                      $"{_opts.CompactRatio * 100:F0}% of it); " +
                      "raise context_window or shrink tool output. Auto-compaction paused."));
        }
    }

    /// <summary>
    /// Compact the session by summarizing older messages into a structured briefing.
    /// The recent tail is bounded by a token budget (not a message count) so a few
    /// large tool outputs can't keep the prompt above the trigger.
    /// </summary>
    public async Task CompactAsync(string trigger, bool force = false)
    {
        var msgs = _session.Snapshot();
        if (msgs.Count < 3) return;

        _sink.Emit(new Event(EventKind.CompactionStarted,
            Compaction: new CompactionInfo(trigger)));

        // Plan the compaction region
        var (head, start, ok) = PlanCompaction(msgs, MinCompactMessages);
        if (!ok)
            (head, start, ok) = PlanCompaction(msgs, 1); // try with just 1 message
        if (!ok || start <= head) return;

        var region = msgs.Skip(head).Take(start - head).ToList().AsReadOnly();

        // Economic check: skip if region is too small to justify an API call
        if (!force && !FoldEconomics(region))
        {
            _sink.Emit(new Event(EventKind.CompactionDone,
                Compaction: new CompactionInfo(trigger, 0, null)));
            return;
        }

        // Summarize using the provider itself
        var summary = await SummarizeAsync(region);
        if (summary == null) return;

        // Build compacted message list: system + summary + recent tail
        var compacted = new List<Message>();
        compacted.AddRange(msgs.Take(head));
        compacted.Add(new Message
        {
            Role = Role.User,
            Content = $"{SummaryTagOpen}\n" +
                      "Summary of earlier conversation (older messages were compacted to save context):\n" +
                      $"{summary}\n" +
                      $"{SummaryTagClose}",
        });
        compacted.AddRange(msgs.Skip(start));

        _session.Replace(compacted.AsReadOnly());
        _session.IncrementRewrite();

        var compactedCount = msgs.Count - compacted.Count;
        _sink.Emit(new Event(EventKind.CompactionDone,
            Compaction: new CompactionInfo(trigger, compactedCount, summary)));
    }

    /// <summary>
    /// Plan the compaction region. head = leading messages kept verbatim (system prompt).
    /// start = where the preserved recent tail begins. Messages [head, start) are compacted.
    /// The tail is bounded by a token budget so large tool outputs can't prevent compaction.
    /// </summary>
    internal (int Head, int Start, bool Ok) PlanCompaction(IReadOnlyList<Message> msgs, int min)
    {
        var head = 0;
        if (msgs.Count > 0 && msgs[0].Role == Role.System)
            head = 1;

        var start = 0;
        if (_opts.ContextWindow > 0)
        {
            var budget = DefaultTailTokens;
            var maxByWin = (int)(_opts.ContextWindow * DefaultCompactTarget);
            if (maxByWin < budget) budget = maxByWin;

            start = TailStart(msgs, head, budget, TokPerChar(), TailFloor());
        }
        else
        {
            // No window — keep a fixed count, aligned off tool messages
            start = msgs.Count - TailFloor();
            while (start > head && start < msgs.Count && msgs[start].Role == Messages.Role.Tool)
                start--;
        }

        if (start < head) start = head;
        if (start - head < min)
            return (head, start, false);

        return (head, start, true);
    }

    /// <summary>
    /// Walk newest→oldest, growing the verbatim tail until the next message would
    /// push the token estimate past budgetTokens (but never below tailFloor messages).
    /// Aligns the boundary back off any tool message so the tail doesn't begin with
    /// an orphan whose assistant tool_calls were summarized away.
    /// </summary>
    private static int TailStart(IReadOnlyList<Message> msgs, int head, int budgetTokens,
        double tokPerChar, int minKeep)
    {
        var start = msgs.Count;
        var acc = 0;
        for (int i = msgs.Count - 1; i > head; i--)
        {
            var c = (int)(MsgChars(msgs[i]) * tokPerChar);
            if (msgs.Count - i > minKeep && acc + c > budgetTokens)
                break;
            acc += c;
            start = i;
        }
        // Align off tool messages
        while (start > head && start < msgs.Count && msgs[start].Role == Messages.Role.Tool)
            start--;
        return start;
    }

    /// <summary>
    /// Estimate tokens per character from the last turn's usage, so per-message
    /// estimates track the real provider tokenizer. Falls back to ~4 chars/token.
    /// </summary>
    private double TokPerChar()
    {
        var u = _lastUsage;
        if (u != null && u.PromptTokens > 0)
        {
            var c = CharsOfMessages(_session.Snapshot());
            if (c > 0)
            {
                var r = (double)u.PromptTokens / c;
                if (r > 0.05 && r < 2)
                    return r;
            }
        }
        return FallbackTokPerChar;
    }

    private int TailFloor() => _opts.RecentKeep > MinRecentKeep ? _opts.RecentKeep : MinRecentKeep;

    /// <summary>
    /// Estimate whether compacting the region saves enough tokens (≥400) to justify
    /// the API call for summarization.
    /// </summary>
    internal static bool FoldEconomics(IReadOnlyList<Message> region)
    {
        return EstimateTokens(region) >= MinFoldTokens;
    }

    /// <summary>
    /// Rough token estimation for a list of messages.
    /// </summary>
    internal static int EstimateTokens(IReadOnlyList<Message> msgs)
    {
        var total = 0;
        foreach (var m in msgs)
        {
            total += 4; // message framing overhead
            total += EstimateTextTokens(m.Content);
            total += EstimateTextTokens(m.ReasoningContent);
            total += EstimateTextTokens(m.Name);
            total += EstimateTextTokens(m.ToolCallId);
            if (m.ToolCalls != null)
            {
                foreach (var tc in m.ToolCalls)
                {
                    total += 8;
                    total += EstimateTextTokens(tc.Id);
                    total += EstimateTextTokens(tc.Name);
                    total += EstimateTextTokens(tc.Arguments);
                }
            }
        }
        return total;
    }

    internal static int EstimateTextTokens(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        // Conservative: English ~4 bytes/token, CJK ~1 rune/token
        var bytes = Encoding.UTF8.GetByteCount(s);
        var runes = s.Length;
        return Math.Max(runes, (bytes + 3) / 4);
    }

    private static int MsgChars(Message m)
    {
        var n = m.Content?.Length ?? 0;
        if (m.ToolCalls != null)
        {
            foreach (var tc in m.ToolCalls)
            {
                n += tc.Name.Length;
                n += tc.Arguments.Length;
            }
        }
        return n;
    }

    private static int CharsOfMessages(IReadOnlyList<Message> msgs)
    {
        var n = 0;
        foreach (var m in msgs)
            n += MsgChars(m);
        return n;
    }

    /// <summary>
    /// Summarize a region of messages into a structured briefing using the provider.
    /// </summary>
    private async Task<string?> SummarizeAsync(IReadOnlyList<Message> region)
    {
        try
        {
            const string sysContent = """
                You are compacting the earlier part of a coding agent's conversation to save context.
                The agent will keep ONLY your summary (the original messages are dropped), so it must be able to resume the task from it alone.
                Write a briefing under these exact headings, omitting a heading only if it has no content:

                ## Goal
                The user's request and intent, kept close to their own words. Include explicit requirements, constraints, and preferences.

                ## Decisions & rationale
                Key choices made so far and why — so they are not re-litigated or reversed.

                ## Files & code
                Files read or modified, with the specific facts that matter: signatures, line locations, data shapes, and exact edits applied. Be concrete; this is what lets the agent act without re-reading everything.

                ## Commands & outcomes
                Commands run (builds, tests, git) and their relevant results — what passed, what failed, and the error text that matters.

                ## Errors & fixes
                Problems hit and how they were resolved (or not), so the same dead ends are not repeated.

                ## Pending & next step
                What is still in progress or unstarted, and the single most concrete next action to take.

                Rules: be terse — bullet points and fragments, not prose. Preserve identifiers, paths, and numbers exactly. Do NOT invent anything not present in the messages; if something is unknown, leave it out rather than guessing.
                """;

            var request = new ProviderRequest(
                new List<Message>
                {
                    new() { Role = Role.System, Content = sysContent },
                    new() { Role = Role.User, Content = RenderTranscript(region) }
                },
                Array.Empty<ToolSchema>(),
                _opts.Temperature
            );

            var sb = new StringBuilder();
            await foreach (var chunk in _prov.StreamAsync(request))
            {
                if (chunk.Type == ChunkType.Text && chunk.Text != null)
                    sb.Append(chunk.Text);
            }
            return sb.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string RenderTranscript(IReadOnlyList<Message> msgs)
    {
        var sb = new StringBuilder();
        foreach (var m in msgs)
        {
            sb.AppendLine($"[{m.Role}]");
            if (m.Content != null) sb.AppendLine(m.Content);
            if (m.ToolCalls is { Count: > 0 })
                foreach (var tc in m.ToolCalls)
                    sb.AppendLine($"  → {tc.Name}({tc.Arguments})");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Summarize messages from fromIdx onward, replacing them with a single summary.
    /// fromIdx must be a turn boundary (a user message).  No-op when region is empty.
    /// </summary>
    public async Task SummarizeFromAsync(int fromIdx, CancellationToken ct = default)
    {
        var msgs = _session.Snapshot();
        if (fromIdx < 0 || fromIdx >= msgs.Count) return;

        var region = msgs.Skip(fromIdx).ToList().AsReadOnly();
        var summary = await SummarizeAsync(region);
        if (summary == null) return;

        var next = msgs.Take(fromIdx).ToList();
        next.Add(new Message
        {
            Role = Role.User,
            Content = $"Summary of later conversation (compacted from here on):\n{summary}"
        });

        _session.Replace(next.AsReadOnly());
        _sink.Emit(new Event(EventKind.Notice, NoticeLevel: Events.NoticeLevel.Info,
            Text: $"summarized {region.Count} later messages → summary"));
    }

    /// <summary>
    /// Summarize messages before toIdx (after system prompt), replacing them with a
    /// single summary. toIdx must be a turn boundary. No-op when region is empty.
    /// </summary>
    public async Task SummarizeUpToAsync(int toIdx, CancellationToken ct = default)
    {
        var msgs = _session.Snapshot();
        var head = 0;
        if (msgs.Count > 0 && msgs[0].Role == Role.System)
            head = 1;

        if (toIdx <= head || toIdx > msgs.Count) return;

        var region = msgs.Skip(head).Take(toIdx - head).ToList().AsReadOnly();
        var summary = await SummarizeAsync(region);
        if (summary == null) return;

        var next = msgs.Take(head).ToList();
        next.Add(new Message
        {
            Role = Role.User,
            Content = $"Summary of earlier conversation (compacted up to here):\n{summary}"
        });
        next.AddRange(msgs.Skip(toIdx));

        _session.Replace(next.AsReadOnly());
        _sink.Emit(new Event(EventKind.Notice, NoticeLevel: Events.NoticeLevel.Info,
            Text: $"summarized {region.Count} earlier messages → summary"));
    }

    #endregion

    #region Storm Breaker

    private const int StormBreakThreshold = 3;
    private const int RepeatSuccessBreakThreshold = 2;

    private void ApplyStormBreaker(List<ToolCall> calls, ToolOutcome[] outcomes)
    {
        var sig = BatchStormSignature(calls, outcomes);
        if (sig == null)
        {
            _stormSig = "";
            _stormCount = 0;
            return;
        }

        if (sig != _stormSig)
        {
            _stormSig = sig;
            _stormCount = 1;
            return;
        }

        _stormCount++;
        if (_stormCount < StormBreakThreshold) return;

        var subject = calls.Count == 1
            ? $"\"{calls[0].Name}\""
            : $"a batch of {calls.Count} calls";

        outcomes[0] = outcomes[0] with
        {
            Output = outcomes[0].Output +
                $"\n\n[loop guard] {subject} has now failed {_stormCount} times in a row. " +
                "Change approach: use a different tool or fix the arguments."
        };

        _sink.Emit(new Event(EventKind.Notice, NoticeLevel: Events.NoticeLevel.Warn,
            Text: $"loop guard: {subject} failed {_stormCount}x the same way"));
    }

    /// <summary>
    /// Block a repeated write-like success: same tool + same args already
    /// succeeded N times this user turn.
    /// </summary>
    private (string Output, bool Blocked) RepeatedSuccessBlock(ToolCall call, ITool tool)
    {
        var sig = RepeatSuccessSignature(call, tool);
        if (sig == null || _repeatSuccessCounts == null)
            return ("", false);

        if (!_repeatSuccessCounts.TryGetValue(sig, out var count))
            return ("", false);

        if (count < RepeatSuccessBreakThreshold)
            return ("", false);

        return ($"blocked: [loop guard] \"{call.Name}\" has already succeeded {count} times with the same write-like arguments in this user turn. Re-running it is unlikely to help and may burn tokens or repeat file writes. Change approach: use edit_file or multi_edit for file changes, verify with a read/test command, or explain the blocker in your final answer.", true);
    }

    /// <summary>
    /// Record a successful write-like call so repeated identical ones are blocked.
    /// </summary>
    private void RecordRepeatSuccess(ToolCall call, ITool tool)
    {
        var sig = RepeatSuccessSignature(call, tool);
        if (sig == null) return;

        _repeatSuccessCounts ??= new Dictionary<string, int>();
        _repeatSuccessCounts[sig] = _repeatSuccessCounts.GetValueOrDefault(sig) + 1;
    }

    /// <summary>
    /// Compute a repeat-success signature for write-like tools.
    /// Returns null for read-only tools, non-write bash commands, etc.
    /// </summary>
    private static string? RepeatSuccessSignature(ToolCall call, ITool tool)
    {
        if (tool.IsReadOnly) return null;

        switch (call.Name)
        {
            case "write_file":
            case "edit_file":
            case "multi_edit":
                return call.Name + "\0" + CanonicalToolArgs(call.Arguments);

            case "bash":
                try
                {
                    using var doc = JsonDocument.Parse(call.Arguments);
                    var root = doc.RootElement;
                    var command = root.TryGetProperty("command", out var cmd) && cmd.ValueKind == JsonValueKind.String
                        ? cmd.GetString() ?? "" : "";
                    var bg = root.TryGetProperty("run_in_background", out var bgEl) && bgEl.ValueKind == JsonValueKind.True;

                    if (bg || !IsShellFileWriteCommand(command))
                        return null;

                    return "bash\0" + NormalizeShellCommand(command);
                }
                catch
                {
                    return null;
                }

            default:
                return null;
        }
    }

    /// <summary>
    /// Canonicalize tool arguments: parse → re-serialize → compact, so cosmetic
    /// differences (key ordering, extra whitespace) don't create false-negative
    /// signatures.
    /// </summary>
    private static string CanonicalToolArgs(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var normalized = JsonSerializer.Serialize(doc.RootElement);
            return normalized;
        }
        catch
        {
            return raw.Trim();
        }
    }

    /// <summary>
    /// Normalize a shell command: collapse whitespace, trim.
    /// </summary>
    private static string NormalizeShellCommand(string command)
    {
        return string.Join(' ', command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Detect whether a bash command writes to the filesystem.
    /// </summary>
    private static bool IsShellFileWriteCommand(string command)
    {
        var lower = command.ToLowerInvariant();
        return ShellHasPythonOpenWrite(lower)
            || lower.Contains("set-content")
            || lower.Contains("add-content")
            || lower.Contains("out-file")
            || lower.Contains("sed -i")
            || lower.Contains("perl -pi")
            || ShellHasWriteRedirect(command);
    }

    private static bool ShellHasPythonOpenWrite(string lower)
    {
        if (!lower.Contains("open(")) return false;
        if (lower.Contains(".write(")) return true;
        var markers = new[] { ", 'w", ", \"w", ", 'a", ", \"a", ", 'x", ", \"x",
            "mode='w", "mode=\"w", "mode='a", "mode=\"a", "mode='x", "mode=\"x" };
        return markers.Any(m => lower.Contains(m));
    }

    private static bool ShellHasWriteRedirect(string command)
    {
        char? quote = null;
        char prev = '\0';
        foreach (var r in command)
        {
            if (quote != null)
            {
                if (r == quote) quote = null;
                prev = r;
                continue;
            }
            if (r == '\'' || r == '"') { quote = r; prev = r; continue; }
            if (r == '>' && prev != '2') return true;
            prev = r;
        }
        return false;
    }

    private static string? BatchStormSignature(List<ToolCall> calls, ToolOutcome[] outcomes)
    {
        if (calls.Count == 0) return null;
        var sb = new StringBuilder();
        for (int i = 0; i < calls.Count; i++)
        {
            if (string.IsNullOrEmpty(outcomes[i].ErrorMsg) || outcomes[i].Blocked)
                return null;
            sb.Append(calls[i].Name);
            sb.Append('\0');
            sb.Append(outcomes[i].ErrorMsg);
            sb.Append('\0');
        }
        return sb.ToString();
    }

    #endregion

    #region Batch Partitioning

    private sealed record ToolBatch(int Start, int End, bool Parallel)
    {
        public int Count => End - Start;
    }

    private List<ToolBatch> PartitionToolCalls(List<ToolCall> calls)
    {
        var batches = new List<ToolBatch>();
        int i = 0;
        while (i < calls.Count)
        {
            if (IsParallelizable(calls[i].Name))
            {
                int start = i;
                while (i < calls.Count && IsParallelizable(calls[i].Name))
                    i++;
                batches.Add(new ToolBatch(start, i, true));
            }
            else
            {
                batches.Add(new ToolBatch(i, i + 1, false));
                i++;
            }
        }
        return batches;
    }

    private bool IsParallelizable(string name)
    {
        var t = _tools.Get(name);
        return t?.IsReadOnly ?? false;
    }

    #endregion

    #region ISubAgentRunner

    async Task<string> ISubAgentRunner.RunSubAgentAsync(string prompt, ToolRegistry tools, int maxSteps, CancellationToken ct)
    {
        var subSession = new Session.AgentSession(SubAgentPrompt);
        var subSink = new Events.DiscardSink();

        var opts = new AgentOptions
        {
            MaxSteps = maxSteps > 0 ? maxSteps : (_opts.MaxSteps > 0 ? Math.Max(5, _opts.MaxSteps / 2) : 0),
            Temperature = _opts.Temperature,
            Pricing = _opts.Pricing,
            ContextWindow = _opts.ContextWindow,
            SoftCompactRatio = _opts.SoftCompactRatio,
            CompactRatio = _opts.CompactRatio,
            CompactForceRatio = _opts.CompactForceRatio,
        };

        var subAgent = new Agent(_prov, tools, subSession, opts, subSink);
        await subAgent.RunAsync(prompt, ct);

        var msgs = subSession.Snapshot();
        for (int i = msgs.Count - 1; i >= 0; i--)
        {
            if (msgs[i].Role == Messages.Role.Assistant && !string.IsNullOrWhiteSpace(msgs[i].Content))
                return msgs[i].Content!;
        }
        return "sub-agent finished without producing a final answer";
    }

    ToolRegistry ISubAgentRunner.GetToolRegistry() => _tools;

    private const string SubAgentPrompt = """
        You are a sub-agent invoked by a parent coding agent to carry out one focused task.
        Use the provided tools to investigate or act. Return a single final answer that is concise
        and self-contained — the parent will see only that answer, not your tool calls or reasoning.
        If you need to ask for clarification, fail with a precise question instead of guessing.
        """;

    #endregion
}
