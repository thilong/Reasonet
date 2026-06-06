using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Reasonet.Tools;

namespace Reasonet.Tools.BuiltIn;

/// <summary>
/// Search for text patterns in files.
/// Cross-platform: ripgrep → grep on Unix; findstr on Windows; .NET fallback everywhere.
/// </summary>
public sealed class GrepTool : ITool
{
    public string Name => "grep";
    public string Description => "Search for a regex pattern in files. On Unix uses ripgrep/grep; on Windows uses findstr. Returns matching file paths and line numbers.";
    public bool IsReadOnly => true;
    public JsonNode Schema => _schema.Value;

    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "pattern": {
              "type": "string",
              "description": "The regex pattern to search for"
            },
            "include": {
              "type": "string",
              "description": "Optional file glob filter, e.g. \"*.cs\" or \"src/**/*.go\""
            }
          },
          "required": ["pattern"],
          "additionalProperties": false
        }
        """)!);

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("pattern", out var patEl) || patEl.ValueKind != JsonValueKind.String)
                return "error: required field \"pattern\" of type string is missing";

            var pattern = patEl.GetString()!;
            if (string.IsNullOrWhiteSpace(pattern))
                return "error: pattern must not be empty";

            var include = "";
            if (root.TryGetProperty("include", out var incEl) && incEl.ValueKind == JsonValueKind.String)
                include = incEl.GetString() ?? "";

            if (Platform.IsWindows)
                return await WindowsSearchAsync(pattern, include, ct);

            // Unix: try ripgrep first, fall back to grep
            if (await IsCommandAvailableAsync("rg", ct))
                return await RunRgAsync(pattern, include, ct);

            if (await IsCommandAvailableAsync("grep", ct))
                return await RunGrepAsync(pattern, include, ct);

            // Final fallback: .NET-based search
            return await DotNetSearchAsync(pattern, include, ct);
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

    /// <summary>
    /// Windows search using findstr (built into Windows).
    /// </summary>
    private static async Task<string> WindowsSearchAsync(string pattern, string include, CancellationToken ct)
    {
        var args = new StringBuilder("/s /n /l ");
        if (!string.IsNullOrEmpty(include))
            args.Append($"/d:{EscapeFindStrArg(include)} ");
        else
            args.Append("/d:* ");
        args.Append('"').Append(pattern.Replace("\"", "\\\"")).Append("\" ");
        args.Append('"').Append(Environment.CurrentDirectory).Append('"');

        var psi = new ProcessStartInfo
        {
            FileName = "findstr",
            Arguments = args.ToString(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(30_000);

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);
        await proc.WaitForExitAsync(cts.Token);

        // findstr exit code 0 = found, 1 = not found
        if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return "no matches";

        return TruncateOutput(stdout);
    }

    private static string EscapeFindStrArg(string arg) =>
        arg.Replace("\"", "\\\"").Replace("*", ".").Replace("?", ".");

    /// <summary>
    /// ripgrep (cross-platform, fastest when installed).
    /// </summary>
    private static async Task<string> RunRgAsync(string pattern, string include, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "rg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--line-number");
        psi.ArgumentList.Add("--with-filename");
        psi.ArgumentList.Add("--color");
        psi.ArgumentList.Add("never");
        psi.ArgumentList.Add("--no-heading");
        psi.ArgumentList.Add("--max-columns");
        psi.ArgumentList.Add("500");
        if (!string.IsNullOrEmpty(include))
        {
            psi.ArgumentList.Add("-g");
            psi.ArgumentList.Add(include);
        }
        psi.ArgumentList.Add(pattern);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(15_000);

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);

        using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts2.CancelAfter(5_000);
        await proc.WaitForExitAsync(cts2.Token);

        if (string.IsNullOrEmpty(stdout))
            return "no matches";

        return TruncateOutput(stdout);
    }

    /// <summary>
    /// System grep (Unix). Falls back from ripgrep when rg is unavailable.
    /// </summary>
    private static async Task<string> RunGrepAsync(string pattern, string include, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "grep",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-rn");
        psi.ArgumentList.Add("--color=never");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(pattern);
        if (!string.IsNullOrEmpty(include))
            psi.ArgumentList.Add($"--include={include}");
        psi.ArgumentList.Add(".");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(30_000);

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);
        await proc.WaitForExitAsync(cts.Token);

        if (string.IsNullOrEmpty(stdout))
            return "no matches";

        return TruncateOutput(stdout);
    }

    /// <summary>
    /// Pure .NET text search — works on all platforms with no external dependencies.
    /// </summary>
    private static async Task<string> DotNetSearchAsync(string pattern, string include, CancellationToken ct)
    {
        var result = new StringBuilder();
        var searchOption = include?.Contains("**") == true
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        var filePattern = string.IsNullOrEmpty(include) ? "*" : Path.GetFileName(include);
        var rootDir = string.IsNullOrEmpty(include)
            ? "."
            : (Path.GetDirectoryName(include) ?? ".");

        if (!Directory.Exists(rootDir))
            rootDir = ".";

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
        }
        catch (ArgumentException ex)
        {
            return $"error: invalid regex pattern: {ex.Message}";
        }

        var files = Directory.EnumerateFiles(
            Path.GetFullPath(rootDir),
            filePattern,
            searchOption
        ).Take(200);

        var count = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (result.Length > 20_000) break;

            try
            {
                var lines = await File.ReadAllLinesAsync(file, Encoding.UTF8, ct);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (regex.IsMatch(lines[i]))
                    {
                        var relPath = Platform.RelativePath(Path.GetFullPath(file));
                        result.AppendLine($"{relPath}:{i + 1}:{lines[i].TrimEnd()}");
                        count++;
                    }
                }
            }
            catch
            {
                // Skip unreadable files
            }
        }

        if (count == 0)
            return "no matches";

        return TruncateOutput(result.ToString());
    }

    /// <summary>
    /// Check if a command is available on PATH (Unix).
    /// </summary>
    private static async Task<bool> IsCommandAvailableAsync(string command, CancellationToken ct)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "which",
                Arguments = command,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc == null) return false;
            await proc.WaitForExitAsync(ct);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string TruncateOutput(string output)
    {
        if (output.Length <= 20_000) return output.TrimEnd();
        return output[..20_000] + $"\n...(truncated, {output.Length} total chars)";
    }
}
