using System.Collections.Concurrent;

namespace Reasonet.Tools;

/// <summary>
/// Manages background shell jobs started by bash(run_in_background=true).
/// </summary>
public static class BackgroundJobs
{
    private static readonly ConcurrentDictionary<string, BackgroundJob> _jobs = new();

    public sealed record BackgroundJob(
        string Id,
        string Label,
        System.Diagnostics.Process Process,
        DateTime StartedAt,
        Task<string> CompletionTask)
    {
        public string? Output { get; set; }
        public bool IsDone => CompletionTask.IsCompleted;
        public string Status => IsDone
            ? (CompletionTask.IsFaulted ? "failed" : "done")
            : "running";
    }

    private static int _nextId;

    public static string Register(string label, System.Diagnostics.Process proc, Task<string> completionTask)
    {
        var id = $"job_{Interlocked.Increment(ref _nextId)}";
        _jobs[id] = new BackgroundJob(id, label, proc, DateTime.UtcNow, completionTask);
        return id;
    }

    public static BackgroundJob? Get(string id) =>
        _jobs.TryGetValue(id, out var job) ? job : null;

    public static IReadOnlyList<BackgroundJob> ListAll() => _jobs.Values.ToList().AsReadOnly();

    public static bool Kill(string id)
    {
        if (!_jobs.TryGetValue(id, out var job)) return false;
        if (!job.Process.HasExited)
        {
            try { job.Process.Kill(entireProcessTree: true); } catch { }
        }
        return true;
    }

    public static void Cleanup(string id) => _jobs.TryRemove(id, out _);
}
