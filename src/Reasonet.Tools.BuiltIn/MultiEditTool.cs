using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Reasonet.Tools;

namespace Reasonet.Tools.BuiltIn;

/// <summary>
/// Apply a list of edits to a single file atomically: each edit runs against the
/// result of the previous one, all in memory; the file is rewritten only if every
/// edit succeeds. Cheaper and safer than chaining edit_file calls.
/// </summary>
public sealed class MultiEditTool : ITool
{
    public string Name => "multi_edit";
    public string Description => "Apply a list of edits to a single file atomically: each edit runs against the result of the previous one, in memory; the file is rewritten only if every edit succeeds. Cheaper and safer than chaining edit_file calls.";
    public bool IsReadOnly => false;

    public JsonNode Schema => _schema.Value;
    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "The absolute or relative path to the file to edit." },
            "edits": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "old_string": { "type": "string", "description": "The exact text to replace (must appear exactly once)." },
                  "new_string": { "type": "string", "description": "The replacement text." }
                },
                "required": ["old_string", "new_string"]
              },
              "description": "List of edits to apply in order. Each old_string is matched against the result of the previous edit."
            }
          },
          "required": ["path", "edits"],
          "additionalProperties": false
        }
        """)!);

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("path", out var pathEl) || pathEl.ValueKind != JsonValueKind.String)
                return "error: required field \"path\" of type string is missing";

            if (!root.TryGetProperty("edits", out var edits) || edits.ValueKind != JsonValueKind.Array)
                return "error: required field \"edits\" of type array is missing";

            var rawPath = pathEl.GetString()!;
            if (string.IsNullOrWhiteSpace(rawPath))
                return "error: path must not be empty";

            var path = Path.GetFullPath(rawPath);
            if (!File.Exists(path))
                return $"error: file not found: {path}";

            // Read content — work in memory
            var content = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            var totalAdded = 0;
            var totalRemoved = 0;
            var editCount = 0;

            foreach (var edit in edits.EnumerateArray())
            {
                if (!edit.TryGetProperty("old_string", out var oldEl) || oldEl.ValueKind != JsonValueKind.String)
                    return $"error: edit {editCount} is missing required field \"old_string\"";
                if (!edit.TryGetProperty("new_string", out var newEl) || newEl.ValueKind != JsonValueKind.String)
                    return $"error: edit {editCount} is missing required field \"new_string\"";

                var oldStr = oldEl.GetString()!;
                var newStr = newEl.GetString()!;

                if (oldStr.Length == 0)
                    return $"error: edit {editCount}: old_string must not be empty";

                var idx = content.IndexOf(oldStr, StringComparison.Ordinal);
                if (idx < 0)
                    return $"error: edit {editCount}: old_string not found in file";

                // Check for multiple occurrences
                var secondIdx = content.IndexOf(oldStr, idx + oldStr.Length, StringComparison.Ordinal);
                if (secondIdx >= 0)
                    return $"error: edit {editCount}: old_string appears more than once";

                content = content[..idx] + newStr + content[(idx + oldStr.Length)..];
                totalAdded += newStr.Count(c => c == '\n');
                totalRemoved += oldStr.Count(c => c == '\n');
                editCount++;
            }

            // All edits succeeded — write once
            await File.WriteAllTextAsync(path, content, Encoding.UTF8, ct);

            if (editCount == 0)
                return "no edits provided";

            return $"Applied {editCount} edit(s) to {path} ({totalAdded} lines added, {totalRemoved} lines removed)";
        }
        catch (JsonException ex)
        {
            return $"error: invalid arguments JSON: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"error: permission denied: {ex.Message}";
        }
        catch (IOException ex)
        {
            return $"error: I/O error: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"error: {ex.Message}";
        }
    }
}
