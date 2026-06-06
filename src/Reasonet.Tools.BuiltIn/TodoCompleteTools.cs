using System.Text.Json;
using System.Text.Json.Nodes;
using Reasonet.Tools;

namespace Reasonet.Tools.BuiltIn;

/// <summary>
/// todo_write — Record and update a structured task list for the current work.
/// </summary>
public sealed class TodoWriteTool : ITool
{
    public string Name => "todo_write";
    public string Description => "Record and update a structured task list for the current work. Send the COMPLETE list every call — it replaces the previous one. Keep exactly one item in_progress at a time, and flip an item to completed the moment it's done. Use this to track progress across turns.";
    public bool IsReadOnly => false;

    public JsonNode Schema => _schema.Value;
    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "todos": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "content": { "type": "string", "description": "Description of the task item." },
                  "status": { "type": "string", "enum": ["pending", "in_progress", "completed"], "description": "Status of this item." },
                  "priority": { "type": "integer", "description": "Optional priority (1=highest)." }
                },
                "required": ["content", "status"]
              },
              "description": "The COMPLETE task list — every call replaces the previous one."
            }
          },
          "required": ["todos"],
          "additionalProperties": false
        }
        """)!);

    /// <summary>
    /// In-memory todo state per session. In a full implementation this would be
    /// session-scoped state; for now it's a simple static store.
    /// </summary>
    private static string? _lastTodos;

    public static string? CurrentTodos => _lastTodos;
    public static void ClearTodos() => _lastTodos = null;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("todos", out var todos) || todos.ValueKind != JsonValueKind.Array)
                return Task.FromResult("error: required field \"todos\" of type array is missing");

            var total = 0;
            var completed = 0;
            var inProgress = 0;
            var pending = 0;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Task list:");
            sb.AppendLine();

            foreach (var item in todos.EnumerateArray())
            {
                total++;
                var content = item.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "?";
                var status = item.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "pending" : "pending";

                var marker = status switch
                {
                    "completed" => "✅",
                    "in_progress" => "▶",
                    _ => "⬜"
                };

                if (status == "completed") completed++;
                else if (status == "in_progress") inProgress++;
                else pending++;

                sb.AppendLine($"  {marker} {content} [{status}]");
            }

            sb.AppendLine();
            sb.AppendLine($"Total: {total} | Completed: {completed} | In progress: {inProgress} | Pending: {pending}");

            _lastTodos = sb.ToString();
            return Task.FromResult(sb.ToString());
        }
        catch (JsonException ex)
        {
            return Task.FromResult($"error: invalid arguments JSON: {ex.Message}");
        }
    }
}

/// <summary>
/// complete_step — Record the evidence-backed completion of ONE step of a plan.
/// </summary>
public sealed class CompleteStepTool : ITool
{
    public string Name => "complete_step";
    public string Description => "Record the evidence-backed completion of ONE step of an approved plan. Call it as you finish each step: sign the step off with PROOF it is done — the verification you ran, the diff/files you changed, or a manual check.";
    public bool IsReadOnly => true; // evidence recording, no filesystem side effects

    public JsonNode Schema => _schema.Value;
    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "step": { "type": "integer", "description": "The step number that was completed (0-based from the plan)." },
            "description": { "type": "string", "description": "Brief description of what was done." },
            "evidence": {
              "type": "string",
              "description": "The proof it's done — verification command output, diff summary, manual check result."
            }
          },
          "required": ["step", "evidence"],
          "additionalProperties": false
        }
        """)!);

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("step", out var stepEl) || stepEl.ValueKind != JsonValueKind.Number)
                return Task.FromResult("error: required field \"step\" of type integer is missing");

            var step = stepEl.GetInt32();
            var description = root.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String
                ? descEl.GetString() ?? "" : "";

            if (!root.TryGetProperty("evidence", out var evEl) || evEl.ValueKind != JsonValueKind.String)
                return Task.FromResult("error: required field \"evidence\" of type string is missing");

            var evidence = evEl.GetString() ?? "";

            // Store evidence - in a full implementation this would go to an evidence ledger
            var result = $"Step {step} completed";
            if (!string.IsNullOrEmpty(description))
                result += $": {description}";

            return Task.FromResult(result + $"\nEvidence: {evidence}");
        }
        catch (JsonException ex)
        {
            return Task.FromResult($"error: invalid arguments JSON: {ex.Message}");
        }
    }
}
