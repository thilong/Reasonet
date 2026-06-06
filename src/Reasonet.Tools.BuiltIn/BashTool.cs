using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Reasonet.Tools;

namespace Reasonet.Tools.BuiltIn;

/// <summary>
/// Execute a shell command and return its output.
/// Cross-platform: cmd.exe on Windows, /bin/bash on Unix.
/// </summary>
public sealed class BashTool : ITool
{
    public string Name => "bash";
    public string Description => "Execute a shell command. Returns stdout, stderr, and exit code. Cross-platform: cmd.exe on Windows, bash on Unix. Use to compile, test, lint, run git, or any terminal operation.";
    public bool IsReadOnly => false;
    public JsonNode Schema => _schema.Value;

    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "command": {
              "type": "string",
              "description": "The shell command to execute"
            },
            "timeout_ms": {
              "type": "integer",
              "description": "Optional timeout in milliseconds (default: 30000, max: 300000)"
            }
          },
          "required": ["command"],
          "additionalProperties": false
        }
        """)!);

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("command", out var cmdEl) || cmdEl.ValueKind != JsonValueKind.String)
                return "error: required field \"command\" of type string is missing";

            var command = cmdEl.GetString()!;
            if (string.IsNullOrWhiteSpace(command))
                return "error: command must not be empty";

            var timeoutMs = 30_000;
            if (root.TryGetProperty("timeout_ms", out var tEl) && tEl.ValueKind == JsonValueKind.Number)
                timeoutMs = Math.Clamp(tEl.GetInt32(), 1_000, 300_000);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            var psi = new ProcessStartInfo
            {
                FileName = Platform.Shell,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(Platform.ShellCommandArg);
            psi.ArgumentList.Add(command);

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var stdoutTask = ReadStreamAsync(proc.StandardOutput, cts.Token);
            var stderrTask = ReadStreamAsync(proc.StandardError, cts.Token);

            await proc.WaitForExitAsync(cts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var exitCode = proc.ExitCode;

            var result = new StringBuilder();
            if (!string.IsNullOrEmpty(stdout))
                result.Append(stdout.TrimEnd('\r', '\n'));
            if (!string.IsNullOrEmpty(stderr))
            {
                if (result.Length > 0) result.Append('\n');
                result.Append("stderr:\n").Append(stderr.TrimEnd('\r', '\n'));
            }
            if (result.Length > 0) result.Append('\n');
            result.Append($"(exit code: {exitCode})");

            if (result.Length > 32 * 1024)
            {
                var truncated = result.ToString(0, 32 * 1024);
                return truncated + $"\n\n...[output truncated at 32KB, total was {result.Length} bytes]...";
            }

            return result.ToString();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return "error: command timed out";
        }
        catch (JsonException ex)
        {
            return $"error: invalid arguments JSON: {ex.Message}";
        }
        catch (Win32Exception ex)
        {
            return $"error: failed to start process: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"error: {ex.Message}";
        }
    }

    private static async Task<string> ReadStreamAsync(StreamReader reader, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            sb.Append(buffer, 0, read);
        }
        return sb.ToString();
    }
}
