using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Reasonet.Tools;

namespace Reasonet.Tools.BuiltIn;

/// <summary>
/// Write content to a file, creating directories as needed.
/// Cross-platform: handles \r\n line endings properly for line counting.
/// </summary>
public sealed class WriteFileTool : ITool, IChangePreviewer
{
    public string Name => "write_file";
    public string Description => "Write content to a file, creating parent directories if they don't exist.";
    public bool IsReadOnly => false;
    public JsonNode Schema => _schema.Value;

    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "The absolute or relative path of the file to write"
            },
            "content": {
              "type": "string",
              "description": "The full content to write to the file"
            }
          },
          "required": ["path", "content"],
          "additionalProperties": false
        }
        """)!);

    public (string Diff, int Added, int Removed)? Preview(string argumentsJson)
    {
        var (path, content) = ParseArgs(argumentsJson);
        if (path == null || content == null) return null;

        var newLineCount = Platform.CountLines(content);

        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path, Encoding.UTF8);
            var existingLines = Platform.CountLines(existing);
            var added = Math.Max(0, newLineCount - existingLines);
            var removed = Math.Max(0, existingLines - newLineCount);
            return ($"Write {newLineCount} lines to {path}", added, removed);
        }
        return ($"Create new file {path} with {newLineCount} lines", newLineCount, 0);
    }

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            var (rawPath, content) = ParseArgs(argumentsJson);
            if (rawPath == null)
                return "error: required field \"path\" of type string is missing";
            if (content == null)
                return "error: required field \"content\" of type string is missing";

            var path = Path.GetFullPath(rawPath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(path, content, Encoding.UTF8, ct);
            var lineCount = Platform.CountLines(content);
            return $"written to {Platform.RelativePath(path)} ({lineCount} lines)";
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

    private static (string? Path, string? Content) ParseArgs(string argumentsJson)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;

        string? path = null, content = null;
        if (root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
            path = p.GetString();
        if (root.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
            content = c.GetString();

        return (path, content);
    }
}
