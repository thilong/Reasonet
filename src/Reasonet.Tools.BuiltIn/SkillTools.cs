using System.Text.Json;
using System.Text.Json.Nodes;
using Reasonet.Skill;
using Reasonet.Tools;

namespace Reasonet.Tools.BuiltIn;

/// <summary>
/// run_skill tool — invokes a playbook from the skills index.
/// </summary>
public sealed class RunSkillTool : ITool
{
    private readonly SkillStore _store;
    private readonly SubagentRunner? _runner;

    public RunSkillTool(SkillStore store, SubagentRunner? runner = null)
    {
        _store = store;
        _runner = runner;
    }

    public string Name => "run_skill";
    public string Description => "Invoke a playbook from the Skills index pinned in the system prompt. For subagent skills, supply `arguments` describing the concrete task. Inline skills fold their body into the turn.";
    public bool IsReadOnly => false; // subagent skills can call writers

    public JsonNode Schema => _schema.Value;
    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "name": {
              "type": "string",
              "description": "Skill identifier from the Skills index (e.g. 'explore', 'review'). Case-sensitive."
            },
            "arguments": {
              "type": "string",
              "description": "Arguments. For inline skills: appended as context. For subagent skills: REQUIRED — becomes the entire task."
            }
          },
          "required": ["name"],
          "additionalProperties": false
        }
        """)!);

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                return "error: required field \"name\" of type string is missing";

            var name = nameEl.GetString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(name))
                return "error: skill name must not be empty";

            var sk = _store.Read(name);
            if (sk == null)
                return $"error: unknown skill \"{name}\"";

            var rawArgs = "";
            if (root.TryGetProperty("arguments", out var argEl) && argEl.ValueKind == JsonValueKind.String)
                rawArgs = argEl.GetString()?.Trim() ?? "";

            if (sk.RunAs == RunAs.Subagent)
            {
                if (_runner == null)
                    return $"error: skill \"{name}\" is runAs=subagent but no subagent runner is configured";
                if (string.IsNullOrEmpty(rawArgs))
                    return $"error: skill \"{name}\" requires 'arguments' for the subagent task";

                return await _runner(sk, rawArgs, ct);
            }

            // Inline: return the body as a tool result
            var result = $"--- Skill: {sk.Name} ---\n\n{sk.Body}";
            if (!string.IsNullOrEmpty(rawArgs))
                result += $"\n\n--- Arguments ---\n{rawArgs}";
            return result;
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

/// <summary>
/// install_skill tool — creates a new skill file.
/// </summary>
public sealed class InstallSkillTool : ITool
{
    private readonly SkillStore _store;
    private readonly InstalledHook? _hook;

    public InstallSkillTool(SkillStore store, InstalledHook? hook = null)
    {
        _store = store;
        _hook = hook;
    }

    public string Name => "install_skill";
    public string Description => "Create a new playbook file. Provide 'name' (identifier) and optionally 'content' (the full markdown body with frontmatter). Defaults to project scope.";
    public bool IsReadOnly => false;

    public JsonNode Schema => _schema.Value;
    private static readonly Lazy<JsonNode> _schema = new(() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "name": {
              "type": "string",
              "description": "Skill identifier — letters, digits, '_', '-', '.' only"
            },
            "content": {
              "type": "string",
              "description": "Optional full skill content (markdown with ---frontmatter---). Uses a default scaffold when empty."
            },
            "scope": {
              "type": "string",
              "description": "Scope: 'project' (default) or 'global'"
            }
          },
          "required": ["name"],
          "additionalProperties": false
        }
        """)!);

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                return "error: required field \"name\" of type string is missing";

            var name = nameEl.GetString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(name))
                return "error: skill name must not be empty";

            var content = "";
            if (root.TryGetProperty("content", out var contEl) && contEl.ValueKind == JsonValueKind.String)
                content = contEl.GetString() ?? "";

            var scope = Scope.Project;
            if (root.TryGetProperty("scope", out var scopeEl) && scopeEl.ValueKind == JsonValueKind.String)
            {
                scope = scopeEl.GetString()?.ToLowerInvariant() switch
                {
                    "global" => Scope.Global,
                    _ => Scope.Project,
                };
            }

            if (string.IsNullOrEmpty(content))
            {
                var (_, err) = _store.Create(name, scope);
                if (err != null) return $"error: {err}";
            }
            else
            {
                var (_, err) = _store.CreateWithContent(name, scope, content);
                if (err != null) return $"error: {err}";
            }

            var rootDir = scope == Scope.Project
                ? Path.Combine(Directory.GetCurrentDirectory(), ".reasonet", "skills")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".reasonet", "skills");

            var path = Path.Combine(rootDir, name + ".md");
            _hook?.Invoke(name, path, scope);
            return $"created skill \"{name}\" at {path}";
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
