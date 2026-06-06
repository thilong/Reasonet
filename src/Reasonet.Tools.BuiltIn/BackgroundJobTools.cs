using System.Text.Json;
using System.Text.Json.Nodes;
using Reasonet.Tools;

namespace Reasonet.Tools.BuiltIn;

/// <summary>
/// bash_output — Read new output from a background job started with bash(run_in_background=true).
/// </summary>
public sealed class BashOutputTool : ITool
{
    public string Name => "bash_output";
    public string Description => "Read new output from a background job started with bash(run_in_background=true). Returns the output produced since the last call for that job, plus its status (running/done/failed/killed). Does not block.";
    public bool IsReadOnly => true;

    public JsonNode Schema => _schema.Value;
    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "id": { "type": "string", "description": "Job ID returned by bash(run_in_background=true)." }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """)!);

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                return Task.FromResult("error: required field \"id\" of type string is missing");

            var id = idEl.GetString() ?? "";
            var job = BackgroundJobs.Get(id);
            if (job == null)
                return Task.FromResult($"error: unknown job id: {id}");

            return Task.FromResult($"[{job.Status}] job {id} ({job.Label})\nTotal output so far pending implementation.");
        }
        catch (JsonException ex)
        {
            return Task.FromResult($"error: invalid arguments JSON: {ex.Message}");
        }
    }
}

/// <summary>
/// kill_shell — Terminate a running background job.
/// </summary>
public sealed class KillShellTool : ITool
{
    public string Name => "kill_shell";
    public string Description => "Terminate a running background job (bash or task) started with run_in_background. A no-op if the job has already finished or the id is unknown.";
    public bool IsReadOnly => false;

    public JsonNode Schema => _schema.Value;
    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "id": { "type": "string", "description": "Job ID to terminate." }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """)!);

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                return Task.FromResult("error: required field \"id\" of type string is missing");

            var id = idEl.GetString() ?? "";
            if (BackgroundJobs.Kill(id))
                return Task.FromResult($"killed job {id}");
            else
                return Task.FromResult($"job {id} not found or already finished");
        }
        catch (JsonException ex)
        {
            return Task.FromResult($"error: invalid arguments JSON: {ex.Message}");
        }
    }
}

/// <summary>
/// wait — Block until background jobs finish, then return each job's status.
/// </summary>
public sealed class WaitTool : ITool
{
    public string Name => "wait";
    public string Description => "Block until background jobs finish. Omit job_ids to wait for every running job. Returns each job's status and final output.";
    public bool IsReadOnly => true;

    public JsonNode Schema => _schema.Value;
    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "job_ids": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Optional list of job IDs to wait for. Omit to wait for all running jobs."
            },
            "timeout_ms": {
              "type": "integer",
              "description": "Optional timeout in milliseconds (default: 60000)."
            }
          },
          "additionalProperties": false
        }
        """)!);

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            List<string> ids;
            if (root.TryGetProperty("job_ids", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
            {
                ids = idsEl.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!)
                    .ToList();
            }
            else
            {
                ids = BackgroundJobs.ListAll()
                    .Where(j => !j.IsDone)
                    .Select(j => j.Id)
                    .ToList();
            }

            var timeoutMs = 60_000;
            if (root.TryGetProperty("timeout_ms", out var tEl) && tEl.ValueKind == JsonValueKind.Number)
                timeoutMs = Math.Clamp(tEl.GetInt32(), 1_000, 300_000);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            if (ids.Count == 0)
                return "no running jobs to wait for";

            // Wait for completion tasks
            var tasks = ids
                .Select(id => BackgroundJobs.Get(id))
                .Where(j => j != null && !j.IsDone)
                .Select(j => j!.CompletionTask)
                .ToArray();

            if (tasks.Length == 0)
                return "all specified jobs have already finished";

            try
            {
                await Task.WhenAll(tasks).WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout
            }

            var results = new System.Text.StringBuilder();
            foreach (var id in ids)
            {
                var job = BackgroundJobs.Get(id);
                if (job == null)
                {
                    results.AppendLine($"  {id}: unknown");
                    continue;
                }
                var status = job.Status;
                var output = job.CompletionTask.IsCompletedSuccessfully
                    ? job.CompletionTask.Result
                    : "";
                results.AppendLine($"  {id} ({job.Label}): {status}");
                if (!string.IsNullOrEmpty(output))
                {
                    if (output.Length > 1000)
                        output = output[..1000] + $"\n  ... (truncated, {output.Length} total chars)";
                    results.AppendLine($"    {string.Join("\n    ", output.Split('\n'))}");
                }
            }

            return results.ToString().TrimEnd();
        }
        catch (JsonException ex)
        {
            return $"error: invalid arguments JSON: {ex.Message}";
        }
    }
}
