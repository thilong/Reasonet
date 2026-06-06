using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Reasonet.Tools;

namespace Reasonet.Tools.BuiltIn;

/// <summary>
/// Read the contents of a file from the filesystem. Cross-platform path handling.
/// </summary>
public sealed class ReadFileTool : ITool
{
    public string Name => "read_file";
    public string Description => "Read the contents of a file at the given path.";
    public bool IsReadOnly => true;
    public JsonNode Schema => _schema.Value;

    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "The absolute or relative path to the file to read"
            }
          },
          "required": ["path"],
          "additionalProperties": false
        }
        """)!);

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("path", out var pathEl) || pathEl.ValueKind != JsonValueKind.String)
                return Task.FromResult("error: required field \"path\" of type string is missing");

            var rawPath = pathEl.GetString()!;
            if (string.IsNullOrWhiteSpace(rawPath))
                return Task.FromResult("error: path must not be empty");

            var path = Path.GetFullPath(rawPath);

            if (!File.Exists(path))
                return Task.FromResult($"error: file not found: {Platform.RelativePath(path)}");

            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > 10 * 1024 * 1024)
                return Task.FromResult($"error: file too large (>10MB): {Platform.RelativePath(path)}");

            var content = File.ReadAllText(path, Encoding.UTF8);
            return Task.FromResult(content);
        }
        catch (JsonException ex)
        {
            return Task.FromResult($"error: invalid arguments JSON: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult($"error: permission denied: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Task.FromResult($"error: I/O error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"error: {ex.Message}");
        }
    }
}
