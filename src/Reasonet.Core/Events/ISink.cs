namespace Reasonet.Events;

/// <summary>
/// Typed event sink. The agent emits events to it; each frontend implements one.
/// </summary>
public interface ISink
{
    void Emit(Event evt);
}

/// <summary>
/// A sink that discards all events.
/// </summary>
public sealed class DiscardSink : ISink
{
    public static readonly DiscardSink Instance = new();
    public void Emit(Event evt) { }
}

/// <summary>
/// A synchronized sink wrapper for concurrent use.
/// </summary>
public sealed class SyncSink : ISink
{
    private readonly ISink _inner;
    private readonly object _lock = new();

    public SyncSink(ISink inner) => _inner = inner;

    public void Emit(Event evt)
    {
        lock (_lock)
            _inner.Emit(evt);
    }
}

public static class SinkExtensions
{
    public static ISink Sync(this ISink sink) => new SyncSink(sink);
}
