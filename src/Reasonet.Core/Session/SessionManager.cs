using System.Text.Json;
using Reasonet.Messages;

namespace Reasonet.Session;

/// <summary>
/// Information about a saved session for the resume picker.
/// </summary>
public sealed record SessionInfo
{
    public string Path { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime LastActivityAt { get; init; }
    public string Preview { get; init; } = "";
    public int Turns { get; init; }
    public string Model { get; init; } = "";
    public string FileName => System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>
/// Manages session persistence: listing, preview, file naming.
/// </summary>
public static class SessionManager
{
    /// <summary>
    /// Get session directory under the workspace's .reasonet folder.
    /// </summary>
    public static string SessionDir(string? workspaceDir = null)
    {
        workspaceDir ??= Environment.CurrentDirectory;
        var dir = Path.Combine(workspaceDir, ".reasonet", "sessions");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Generate a session file path with timestamp and model name.
    /// e.g. "20260606-103045.000000000-deepseek-v4-flash.jsonl"
    /// </summary>
    public static string NewSessionPath(string dir, string model)
    {
        var safe = model.Replace("/", "-").Replace("\\", "-");
        if (string.IsNullOrWhiteSpace(safe))
            safe = "session";
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss.fffffff");
        return System.IO.Path.Combine(dir, $"{timestamp}-{safe}.jsonl");
    }

    /// <summary>
    /// List all sessions, newest first, with previews.
    /// </summary>
    public static IReadOnlyList<SessionInfo> ListSessions(string? dir = null)
    {
        var sessionDir = dir ?? SessionDir();
        if (!Directory.Exists(sessionDir))
            return Array.Empty<SessionInfo>();

        var results = new List<SessionInfo>();

        foreach (var file in Directory.GetFiles(sessionDir, "*.jsonl"))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Length == 0) continue;

                var (preview, turns) = ExtractPreview(file);
                if (turns == 0) continue; // skip empty sessions

                var model = ExtractModelFromFilename(file);

                results.Add(new SessionInfo
                {
                    Path = file,
                    CreatedAt = info.CreationTimeUtc,
                    LastActivityAt = info.LastWriteTimeUtc,
                    Preview = preview,
                    Turns = turns,
                    Model = model,
                });
            }
            catch
            {
                // Skip unreadable files
            }
        }

        // Sort newest first
        results.Sort((a, b) => b.LastActivityAt.CompareTo(a.LastActivityAt));
        return results.AsReadOnly();
    }

    /// <summary>
    /// Extract preview and turn count from a JSONL session file.
    /// Reads the first user message as preview and counts all user messages as turns.
    /// </summary>
    internal static (string Preview, int Turns) ExtractPreview(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            string? firstUser = null;
            var turns = 0;

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var msg = JsonSerializer.Deserialize<Message>(line);
                if (msg == null) continue;

                if (msg.Role == Role.User)
                {
                    turns++;
                    if (firstUser == null)
                    {
                        var text = msg.Content ?? "";
                        if (text.Length > 80)
                            text = text[..77] + "\u2026";
                        firstUser = text;
                    }
                }
            }

            return (firstUser ?? "", turns);
        }
        catch
        {
            return ("", 0);
        }
    }

    /// <summary>
    /// Extract model name from filename like "20260606-103045-deepseek-v4-flash.jsonl"
    /// </summary>
    internal static string ExtractModelFromFilename(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        // Remove timestamp prefix: "20260606-103045.000000000-deepseek-v4-flash" → "deepseek-v4-flash"
        var dashIdx = name.IndexOf('-');
        if (dashIdx >= 0)
        {
            var after = name[(dashIdx + 1)..];
            // If there's another dash (seconds part), skip it too
            var secondDash = after.IndexOf('-');
            if (secondDash >= 0)
                return after[(secondDash + 1)..];
            return after;
        }
        return name;
    }
}
