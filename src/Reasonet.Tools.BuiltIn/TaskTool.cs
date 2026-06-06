using System.Text.Json;
using System.Text.Json.Nodes;
using Reasonet.Agent;
using Reasonet.Tools;

namespace Reasonet.Tools.BuiltIn;

/// <summary>
/// TaskTool spawns a sub-agent in its own session for a focused sub-task.
/// Takes an ISubAgentRunner that the CLI wires at construction time.
/// </summary>
public sealed class TaskTool : ITool
{
    private readonly ISubAgentRunner? _runner;

    public TaskTool(ISubAgentRunner? runner = null) => _runner = runner;

    public string Name => "task";
    public string Description => "Spawn a sub-agent for a focused sub-task. The sub-agent runs in its own session with the same provider and a filtered tool list (defaults to every parent tool except subagent/skill meta-tools). Only its final answer is returned. Use to keep long exploration out of the parent's context budget, or delegate self-contained work.";
    public bool IsReadOnly => false;

    public JsonNode Schema => _schema.Value;
    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "prompt": { "type": "string", "description": "What the sub-agent should accomplish. Be specific — the sub-agent does not see this conversation." },
            "description": { "type": "string", "description": "Short label (3-7 words). Surfaced in the dispatch line." },
            "tools": { "type": "array", "items": { "type": "string" }, "description": "Optional tool whitelist." },
            "max_steps": { "type": "integer", "description": "Optional cap. Defaults to half the parent's cap (min 5).", "minimum": 1 },
            "run_in_background": { "type": "boolean", "description": "Run asynchronously: returns job id immediately." }
          },
          "required": ["prompt"],
          "additionalProperties": false
        }
        """)!);

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            var prompt = JsonString(root, "prompt");
            if (string.IsNullOrEmpty(prompt))
                return "error: required field \"prompt\" is missing";

            var toolNames = JsonStringArray(root, "tools");

            if (_runner == null)
                return "error: sub-agent runner is not available in this session";

            var parentReg = _runner.GetToolRegistry();
            var subReg = ToolFilter.Filter(parentReg, toolNames);

            return await _runner.RunSubAgentAsync(prompt!, subReg, 0, ct);
        }
        catch (JsonException ex)
        {
            return $"error: invalid arguments JSON: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"error: {ex.Message}";
        }
    }

    private static string? JsonString(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string>? JsonStringArray(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        var list = new List<string>();
        foreach (var item in v.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString()!);
        return list.Count > 0 ? list : null;
    }
}
