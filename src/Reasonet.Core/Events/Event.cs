using Reasonet.Providers;

namespace Reasonet.Events;

/// <summary>
/// Kind tags an Event. Read the field(s) documented for that kind.
/// </summary>
public enum EventKind
{
    TurnStarted,
    Reasoning,
    Text,
    Message,
    ToolDispatch,
    ToolResult,
    Usage,
    Notice,
    Phase,
    TurnDone,
    CompactionStarted,
    CompactionDone,
    ToolProgress,
    Retrying
}

/// <summary>
/// Level for Notice events.
/// </summary>
public enum NoticeLevel { Info, Warn }

/// <summary>
/// A tool event payload.
/// </summary>
public sealed record ToolEvent(
    string Id,
    string Name,
    string? Args = null,
    string? Output = null,
    string? Error = null,
    bool IsReadOnly = false,
    bool IsPartial = false,
    bool Truncated = false,
    string? ParentId = null,
    (string Diff, int Added, int Removed)? FileDiff = null
);

/// <summary>
/// Compaction event payload.
/// </summary>
public sealed record CompactionInfo(
    string Trigger,
    int Messages = 0,
    string? Summary = null,
    string? Archive = null
);

/// <summary>
/// A type-safe event emitted by the agent during a turn.
/// </summary>
public sealed record Event(
    EventKind Kind,
    string? Text = null,
    string? Reasoning = null,
    ToolEvent? Tool = null,
    Usage? Usage = null,
    Pricing? Pricing = null,
    NoticeLevel NoticeLevel = NoticeLevel.Info,
    string? Phase = null,
    CompactionInfo? Compaction = null,
    int RetryAttempt = 0,
    int RetryMax = 0,
    Exception? Error = null
);
