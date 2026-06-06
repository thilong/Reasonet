using System.Text.Json;
using System.Text.Json.Nodes;
using Reasonet.Tools;

namespace Reasonet.Tools.BuiltIn;

/// <summary>
/// List files matching a glob pattern. Cross-platform path handling.
/// Uses .NET Directory.EnumerateFiles with SearchOption.AllDirectories for ** patterns.
/// </summary>
public sealed class GlobTool : ITool
{
    public string Name => "glob";
    public string Description => "List files matching a glob pattern. Supports patterns like \"**/*.cs\", \"src/**/*.go\", \"*.json\".";
    public bool IsReadOnly => true;
    public JsonNode Schema => _schema.Value;

    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "pattern": {
              "type": "string",
              "description": "The glob pattern to match, e.g. \"**/*.cs\" or \"src/**/*.go\""
            }
          },
          "required": ["pattern"],
          "additionalProperties": false
        }
        """)!);

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("pattern", out var patEl) || patEl.ValueKind != JsonValueKind.String)
                return Task.FromResult("error: required field \"pattern\" of type string is missing");

            var rawPattern = patEl.GetString()!;
            if (string.IsNullOrWhiteSpace(rawPattern))
                return Task.FromResult("error: pattern must not be empty");

            // Normalize separators to match the OS
            var pattern = rawPattern.Replace('/', Path.DirectorySeparatorChar)
                                    .Replace('\\', Path.DirectorySeparatorChar);

            // Determine whether to search recursively (if pattern uses **)
            var hasRecursive = pattern.Contains("**");
            var searchOption = hasRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            // Split the pattern into a base directory and file match pattern.
            // For "src/**/*.cs": dir="src", filePattern="*.cs"
            // For "**/*.cs": dir=".", filePattern="*.cs"
            // For "*.json": dir=".", filePattern="*.json"
            // For "src/foo/*.go": dir="src/foo", filePattern="*.go"
            var dir = ExtractBaseDirectory(pattern);
            var filePattern = ExtractFileName(pattern);

            var baseDir = Path.GetFullPath(dir);
            if (!Directory.Exists(baseDir))
                return Task.FromResult($"error: directory not found: {baseDir}");

            var results = new List<string>();
            var cwd = Directory.GetCurrentDirectory();

            try
            {
                var files = Directory.EnumerateFiles(baseDir, filePattern, searchOption);
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (results.Count >= 200)
                    {
                        results.Add("...(200 files shown, more exist)");
                        break;
                    }

                    // Make path relative to cwd for cleaner display
                    var relPath = MakeRelative(file, cwd);
                    results.Add(relPath);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Some directories may not be accessible — return what we have
            }
            catch (DirectoryNotFoundException)
            {
                // Intermediate directory in pattern doesn't exist
            }

            if (results.Count == 0)
                return Task.FromResult("no matches");

            results.Sort(StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(string.Join('\n', results));
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

    /// <summary>
    /// Extract the base directory from a glob pattern.
    /// "src/**/*.cs" → "src"
    /// "src/foo/*.go" → "src/foo"
    /// "*.json" → "."
    /// </summary>
    private static string ExtractBaseDirectory(string pattern)
    {
        // Find the first segment that is a glob wildcard or the filename
        var parts = pattern.Split(Path.DirectorySeparatorChar);
        var baseParts = new List<string>();

        foreach (var part in parts)
        {
            if (part.Contains('*') || part.Contains('?'))
                break;
            baseParts.Add(part);
        }

        var dir = string.Join(Path.DirectorySeparatorChar.ToString(), baseParts);
        return string.IsNullOrEmpty(dir) ? "." : dir;
    }

    /// <summary>
    /// Extract the filename glob from a pattern.
    /// "src/**/*.cs" → "*.cs"
    /// "src/foo/*.go" → "*.go"
    /// "*.json" → "*.json"
    /// "test/**" → "*"  (no extension filter)
    /// </summary>
    private static string ExtractFileName(string pattern)
    {
        var lastSep = pattern.LastIndexOf(Path.DirectorySeparatorChar);
        var fileName = lastSep >= 0 ? pattern[(lastSep + 1)..] : pattern;

        if (string.IsNullOrEmpty(fileName) || fileName == "**")
            return "*";

        return fileName;
    }

    /// <summary>
    /// Make a path relative to the current directory (cross-platform).
    /// </summary>
    private static string MakeRelative(string fullPath, string cwd)
    {
        if (fullPath.StartsWith(cwd, StringComparison.OrdinalIgnoreCase))
        {
            var rel = fullPath[cwd.Length..];
            rel = rel.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (rel.Length > 0) return rel;
        }
        return fullPath;
    }
}
