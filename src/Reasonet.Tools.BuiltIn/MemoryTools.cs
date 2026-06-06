using System.Text.Json;
using System.Text.Json.Nodes;
using Reasonet.Memory;
using Reasonet.Tools;

namespace Reasonet.Tools.BuiltIn;

/// <summary>
/// `remember` tool — saves a durable fact to project auto-memory.
/// </summary>
public sealed class RememberTool : ITool
{
    private readonly MemoryStore _store;

    public RememberTool(MemoryStore store) => _store = store;

    public string Name => "remember";
    public string Description => "Save a durable fact to project memory so it survives across sessions. " +
        "For feedback/project, structure the body with a \"**Why:**\" line and a \"**How to apply:**\" line. " +
        "Before saving, check the loaded memory index for an entry that already covers this — reuse that name to update it. " +
        "Use `forget` to drop one that is now wrong.";
    public bool IsReadOnly => false;

    public JsonNode Schema => _schema.Value;
    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "Short kebab-case slug, e.g. \"prefers-tabs\". Reusing a name overwrites that memory." },
            "title": { "type": "string", "description": "Short human-readable label, e.g. \"Prefers tabs\"." },
            "description": { "type": "string", "description": "One-line hook shown in the index." },
            "type": { "type": "string", "enum": ["user", "feedback", "project", "reference"], "description": "Category of the fact." },
            "body": { "type": "string", "description": "The fact itself (Markdown)." }
          },
          "required": ["description", "body"],
          "additionalProperties": false
        }
        """)!);

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            var name = JsonString(root, "name") ?? "";
            var title = JsonString(root, "title");
            var description = JsonString(root, "description");
            var typeStr = JsonString(root, "type") ?? "project";
            var body = JsonString(root, "body");

            if (string.IsNullOrEmpty(description))
                return Task.FromResult("error: required field \"description\" is missing");
            if (string.IsNullOrEmpty(body))
                return Task.FromResult("error: required field \"body\" is missing");

            // Derive name from description when omitted
            if (string.IsNullOrEmpty(name) && description != null)
                name = MemoryHelpers.Slugify(description);
            if (string.IsNullOrEmpty(name))
                name = "note";

            var fact = new MemoryFact
            {
                Name = name,
                Title = title,
                Description = description!,
                Type = MemoryHelpers.NormalizeType(typeStr ?? "project"),
                Body = body!,
            };

            var path = _store.Save(fact);
            return Task.FromResult($"saved memory \"{name}\" at {path}");
        }
        catch (JsonException ex)
        {
            return Task.FromResult($"error: invalid arguments JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"error: {ex.Message}");
        }
    }

    private static string? JsonString(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}

/// <summary>
/// `forget` tool — deletes a saved memory that is wrong or stale.
/// </summary>
public sealed class ForgetTool : ITool
{
    private readonly MemoryStore _store;

    public ForgetTool(MemoryStore store) => _store = store;

    public string Name => "forget";
    public string Description => "Delete a saved memory by name when it is wrong, stale, or superseded. Use the slug from the memory index.";
    public bool IsReadOnly => false;

    public JsonNode Schema => _schema.Value;
    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "Slug of the memory to delete, as shown in the index." }
          },
          "required": ["name"],
          "additionalProperties": false
        }
        """)!);

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            var name = JsonString(root, "name");
            if (string.IsNullOrEmpty(name))
                return Task.FromResult("error: required field \"name\" is missing");

            _store.Delete(name!);
            return Task.FromResult($"forgot memory \"{name}\"");
        }
        catch (JsonException ex)
        {
            return Task.FromResult($"error: invalid arguments JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"error: {ex.Message}");
        }
    }

    private static string? JsonString(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
