using System.Text.Json;
using Reasonet.Messages;

namespace Reasonet.Session;

/// <summary>
/// Holds the conversation history for one task. Thread-safe for concurrent
/// read/write (the run loop writes, a frontend reads via Snapshot).
/// </summary>
public sealed class AgentSession
{
    private readonly List<Message> _messages = [];
    private readonly object _lock = new();
    private int _rewriteVersion;

    public AgentSession(string? systemPrompt = null)
    {
        if (!string.IsNullOrEmpty(systemPrompt))
            _messages.Add(new Message { Role = Role.System, Content = systemPrompt });
    }

    public void Add(Message message)
    {
        lock (_lock)
            _messages.Add(message);
    }

    public void Replace(IReadOnlyList<Message> messages)
    {
        lock (_lock)
        {
            _messages.Clear();
            _messages.AddRange(messages);
        }
    }

    public IReadOnlyList<Message> Snapshot()
    {
        lock (_lock)
            return _messages.ToList().AsReadOnly();
    }

    public int Count
    {
        get { lock (_lock) return _messages.Count; }
    }

    public int RewriteVersion
    {
        get { lock (_lock) return _rewriteVersion; }
    }

    public void IncrementRewrite()
    {
        lock (_lock) _rewriteVersion++;
    }

    public bool HasContent()
    {
        lock (_lock)
            return _messages.Any(m => m.Role != Role.System);
    }

    /// <summary>
    /// Save the session to a JSONL file (one JSON object per line).
    /// </summary>
    public async Task SaveAsync(string path, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmpPath = Path.Combine(
            Path.GetDirectoryName(path) ?? ".",
            $".session.{Guid.NewGuid():N}.tmp"
        );

        try
        {
            var msgs = Snapshot();
            await using var writer = new StreamWriter(tmpPath);
            foreach (var msg in msgs)
            {
                ct.ThrowIfCancellationRequested();
                var json = JsonSerializer.Serialize(msg);
                await writer.WriteLineAsync(json);
            }
            await writer.FlushAsync(ct);

            // Atomic replace
            if (File.Exists(path))
                File.Replace(tmpPath, path, null);
            else
                File.Move(tmpPath, path);
        }
        catch
        {
            try { File.Delete(tmpPath); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Load a session from a JSONL file.
    /// </summary>
    public static async Task<AgentSession> LoadAsync(string path, CancellationToken ct = default)
    {
        var session = new AgentSession();
        using var reader = new StreamReader(path);
        var line = await reader.ReadLineAsync(ct);
        while (line != null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                var msg = JsonSerializer.Deserialize<Message>(line);
                if (msg != null)
                    session.Add(msg);
            }
            line = await reader.ReadLineAsync(ct);
        }
        return session;
    }

    /// <summary>
    /// Return the system prompt content, or empty string.
    /// </summary>
    public string SystemPrompt()
    {
        lock (_lock)
        {
            foreach (var m in _messages)
                if (m.Role == Role.System && m.Content != null)
                    return m.Content;
            return "";
        }
    }
}
