using System.Text.Json;
using System.Text.Json.Nodes;
using Reasonet.Tools;

namespace Reasonet.Tools.BuiltIn;

/// <summary>
/// List the entries of a directory.
/// </summary>
public sealed class ListDirTool : ITool
{
    public string Name => "ls";
    public string Description => "List the entries of a directory. Directories are shown with a trailing slash; files show their byte size. Set recursive=true to list all nested files depth-first.";
    public bool IsReadOnly => true;

    public JsonNode Schema => _schema.Value;
    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Directory path (default: current directory)." },
            "recursive": { "type": "boolean", "description": "List all nested files depth-first." }
          },
          "additionalProperties": false
        }
        """)!);

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            var path = ".";
            if (root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
                path = p.GetString() ?? ".";

            var recursive = root.TryGetProperty("recursive", out var r) && r.ValueKind == JsonValueKind.True;

            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
                return Task.FromResult($"error: directory not found: {fullPath}");

            var results = new List<string>();
            var cwd = Directory.GetCurrentDirectory();

            if (recursive)
            {
                var files = Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)
                    .Take(500);
                foreach (var f in files)
                    results.Add(MakeRelative(f, cwd));
            }
            else
            {
                var entries = Directory.EnumerateFileSystemEntries(fullPath)
                    .Take(500);
                foreach (var e in entries)
                {
                    var rel = MakeRelative(e, cwd);
                    try
                    {
                        if (Directory.Exists(e))
                            results.Add(rel + "/");
                        else
                        {
                            var info = new FileInfo(e);
                            results.Add($"{rel} ({FormatSize(info.Length)})");
                        }
                    }
                    catch
                    {
                        results.Add(rel);
                    }
                }
            }

            if (results.Count == 0)
                return Task.FromResult("(empty directory)");

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

    private static string MakeRelative(string fullPath, string cwd)
    {
        if (fullPath.StartsWith(cwd, StringComparison.OrdinalIgnoreCase))
        {
            var rel = fullPath[cwd.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return rel.Length > 0 ? rel : fullPath;
        }
        return fullPath;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes}B",
        < 1024 * 1024 => $"{bytes / 1024}KB",
        _ => $"{bytes / (1024.0 * 1024):F1}MB"
    };
}

/// <summary>
/// Fetch a URL and return its text content.
/// </summary>
public sealed class WebFetchTool : ITool
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public string Name => "web_fetch";
    public string Description => "Fetch a URL over HTTPS/HTTP and return its text content. HTML pages are reduced to readable text; JSON / plain text / markdown bodies come back verbatim.";
    public bool IsReadOnly => true;

    public JsonNode Schema => _schema.Value;
    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "url": { "type": "string", "description": "The URL to fetch. HTTPS preferred." },
            "max_length": { "type": "integer", "description": "Maximum characters to return (default: 20000)." }
          },
          "required": ["url"],
          "additionalProperties": false
        }
        """)!);

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("url", out var urlEl) || urlEl.ValueKind != JsonValueKind.String)
                return "error: required field \"url\" of type string is missing";

            var url = urlEl.GetString()!;
            if (string.IsNullOrWhiteSpace(url))
                return "error: url must not be empty";

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return "error: url must start with http:// or https://";

            var maxLength = 20_000;
            if (root.TryGetProperty("max_length", out var ml) && ml.ValueKind == JsonValueKind.Number)
                maxLength = Math.Clamp(ml.GetInt32(), 1_000, 100_000);

            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var isHtml = contentType.Contains("html", StringComparison.OrdinalIgnoreCase);

            var raw = await response.Content.ReadAsStringAsync(ct);
            string text;

            if (isHtml)
            {
                // Simple HTML-to-text: strip tags, collapse whitespace
                text = System.Text.RegularExpressions.Regex.Replace(raw, @"<[^>]+>", " ");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
                text = text.Trim();
            }
            else
            {
                text = raw.Trim();
            }

            if (text.Length > maxLength)
                text = text[..maxLength] + $"\n\n...(truncated, {raw.Length} total chars)";

            return text;
        }
        catch (HttpRequestException ex)
        {
            return $"error: HTTP request failed: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            return "error: request timed out";
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
}
