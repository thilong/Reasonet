using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Reasonet.Tools;

namespace Reasonet.Tools.BuiltIn;

/// <summary>
/// Apply a string replacement edit to a file.
/// Cross-platform: old_string/new_string comparison works with any line endings.
/// </summary>
public sealed class EditFileTool : ITool, IChangePreviewer
{
    public string Name => "edit_file";
    public string Description => "Replace an exact string match in a file with new content. The old_string must appear exactly once. Use for targeted edits without rewriting the entire file.";
    public bool IsReadOnly => false;
    public JsonNode Schema => _schema.Value;

    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "The absolute or relative path of the file to edit"
            },
            "old_string": {
              "type": "string",
              "description": "The exact text to replace (must appear exactly once)"
            },
            "new_string": {
              "type": "string",
              "description": "The replacement text"
            }
          },
          "required": ["path", "old_string", "new_string"],
          "additionalProperties": false
        }
        """)!);

    public (string Diff, int Added, int Removed)? Preview(string argumentsJson)
    {
        var (path, oldStr, newStr) = ParseArgs(argumentsJson);
        if (path == null || oldStr == null || newStr == null) return null;

        var oldLines = Platform.CountLines(oldStr);
        var newLines = Platform.CountLines(newStr);
        var removed = Math.Max(0, oldLines - 1);
        var added = Math.Max(0, newLines - 1);
        return ($"Edit {Path.GetFileName(path)}: {oldLines} → {newLines} lines", added, removed);
    }

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            var (rawPath, oldStr, newStr) = ParseArgs(argumentsJson);
            if (rawPath == null)
                return "error: required field \"path\" of type string is missing";
            if (oldStr == null)
                return "error: required field \"old_string\" of type string is missing";
            if (newStr == null)
                return "error: required field \"new_string\" of type string is missing";

            if (oldStr.Length == 0)
                return "error: old_string must not be empty";

            var path = Path.GetFullPath(rawPath);

            if (!File.Exists(path))
                return $"error: file not found: {path}";

            var content = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            // Normalize both to \n for consistent matching
            var normalizedContent = Platform.NormalizeLineEndings(content);
            var normalizedOld = Platform.NormalizeLineEndings(oldStr);

            var idx = normalizedContent.IndexOf(normalizedOld, StringComparison.Ordinal);
            if (idx < 0)
                return "error: old_string not found in the file";

            // Check for multiple occurrences (in normalized form)
            var secondIdx = normalizedContent.IndexOf(normalizedOld, idx + normalizedOld.Length, StringComparison.Ordinal);
            if (secondIdx >= 0)
                return "error: old_string appears more than once in the file — use a more specific string";

            // Map back to original content for the actual edit, preserving original line endings
            var normalizedNew = Platform.NormalizeLineEndings(newStr);
            var newContent = content[..idx] + normalizedNew + content[(idx + normalizedOld.Length)..];

            await File.WriteAllTextAsync(path, newContent, Encoding.UTF8, ct);

            var oldLines = Platform.CountLines(oldStr);
            var newLines = Platform.CountLines(newStr);
            return $"Applied edit to {Platform.RelativePath(path)} ({oldLines} lines → {newLines} lines)";
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

    private static (string? Path, string? OldString, string? NewString) ParseArgs(string argumentsJson)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;

        string? path = null, oldStr = null, newStr = null;
        if (root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
            path = p.GetString();
        if (root.TryGetProperty("old_string", out var o) && o.ValueKind == JsonValueKind.String)
            oldStr = o.GetString();
        if (root.TryGetProperty("new_string", out var n) && n.ValueKind == JsonValueKind.String)
            newStr = n.GetString();

        return (path, oldStr, newStr);
    }
}
