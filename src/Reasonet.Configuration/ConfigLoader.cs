using System.Globalization;
using Reasonet.Providers;

namespace Reasonet.Configuration;

/// <summary>
/// Loads Reasonet configuration from TOML.
/// Resolution: only <workspace>/.reasonet/config.toml + built-in defaults.
/// </summary>
public static class ConfigLoader
{
    public static ReasonetConfig Load(string? workspaceDir = null)
    {
        workspaceDir ??= Environment.CurrentDirectory;
        var path = Path.Combine(workspaceDir, ".reasonet", "config.toml");

        if (File.Exists(path))
            return LoadFromFile(path);

        return new ReasonetConfig();
    }

    public static ReasonetConfig LoadFromFile(string path)
    {
        var text = File.ReadAllText(path);
        var parser = new SimpleTomlParser(text);

        return new ReasonetConfig
        {
            DefaultModel = parser.GetString("default_model", ""),
            Language = parser.GetString("language", ""),
            Agent = new AgentConfig
            {
                SystemPrompt = parser.GetSectionString("agent", "system_prompt", AgentConfig.DefaultSystemPrompt),
                MaxSteps = parser.GetSectionInt("agent", "max_steps", 25),
                Temperature = parser.GetSectionDouble("agent", "temperature", 0.0),
                AutoPlan = parser.GetSectionString("agent", "auto_plan", "off"),
                SoftCompactRatio = parser.GetSectionDouble("agent", "soft_compact_ratio", 0.5),
                CompactRatio = parser.GetSectionDouble("agent", "compact_ratio", 0.8),
                CompactForceRatio = parser.GetSectionDouble("agent", "compact_force_ratio", 0.9),
                PlannerModel = parser.GetSectionStringOrNull("agent", "planner_model"),
            },
            Providers = parser.ParseProviders(),
            Tools = new ToolsConfig
            {
                Enabled = parser.GetSectionStringList("tools", "enabled"),
            },
            Permissions = new PermissionsConfig
            {
                Mode = parser.GetSectionString("permissions", "mode", "allow"),
            },
            Network = new NetworkConfig
            {
                Proxy = parser.GetSectionStringOrNull("network", "proxy"),
            },
            Skills = new SkillsConfig
            {
                DisabledSkills = parser.GetSectionStringList("skills", "disabled_skills"),
            }
        };
    }
}

/// <summary>
/// Minimal TOML parser for Reasonet configuration.
/// </summary>
internal sealed class SimpleTomlParser
{
    private readonly Dictionary<string, string> _global = new();
    private readonly Dictionary<string, Dictionary<string, string>> _sections = new();
    private readonly List<Dictionary<string, string>> _providerEntries = new();
    private string _currentSection = "";
    private bool _inProviderArray;

    public SimpleTomlParser(string text) => Parse(text);

    private void Parse(string text)
    {
        var lines = text.Split('\n');
        string? multilineKey = null;
        var multilineValue = new System.Text.StringBuilder();
        var inMultiline = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (inMultiline)
            {
                if (line.Contains("\"\"\""))
                {
                    var idx = line.IndexOf("\"\"\"");
                    multilineValue.AppendLine(line[..idx]);
                    SetValue(multilineKey!, multilineValue.ToString().TrimEnd());
                    inMultiline = false;
                    multilineKey = null;
                    multilineValue.Clear();
                    var rest = line[(idx + 3)..].Trim();
                    if (!string.IsNullOrEmpty(rest)) continue;
                }
                else
                {
                    multilineValue.AppendLine(line);
                    continue;
                }
                continue;
            }

            var commentIdx = line.IndexOf('#');
            if (commentIdx >= 0) line = line[..commentIdx];
            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith('['))
            {
                inMultiline = false;
                multilineKey = null;
                _inProviderArray = rawLine.TrimStart().StartsWith("[[");
                _currentSection = line.TrimStart('[').TrimEnd(']').Trim();

                if (_inProviderArray)
                {
                    _providerEntries.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                }
                else if (!_sections.ContainsKey(_currentSection))
                {
                    _sections[_currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                continue;
            }

            var eqIdx = line.IndexOf('=');
            if (eqIdx < 0) continue;

            var key = line[..eqIdx].Trim();
            var value = line[(eqIdx + 1)..].Trim();

            // Enter multi-line mode when value starts with """ but isn't closed on the same line.
            // Handles both: key = """\ncontent  (value is just """)
            // And:          key = """content""" (inline multi-line)
            if (value.Trim() == "\"\"\"" ||
                (value.Contains("\"\"\"") && !value.TrimEnd().EndsWith("\"\"\"")))
            {
                inMultiline = true;
                multilineKey = key;
                var startIdx = value.IndexOf("\"\"\"") + 3;
                var after = value[startIdx..].TrimStart();
                if (!string.IsNullOrEmpty(after))
                    multilineValue.AppendLine(after);
                continue;
            }

            SetValue(key, value);
        }

        if (inMultiline && multilineKey != null && multilineValue.Length > 0)
            SetValue(multilineKey, multilineValue.ToString().TrimEnd());
    }

    private void SetValue(string key, string value)
    {
        value = value.Trim();
        if (value.StartsWith('"') && value.EndsWith('"'))
            value = value[1..^1];
        else if (value.StartsWith('\'') && value.EndsWith('\''))
            value = value[1..^1];

        if (_inProviderArray && _providerEntries.Count > 0)
            _providerEntries[^1][key] = value;
        else if (!string.IsNullOrEmpty(_currentSection) && _sections.TryGetValue(_currentSection, out var sec))
            sec[key] = value;
        else
            _global[key] = value;
    }

    public string GetString(string key, string defaultValue) =>
        _global.TryGetValue(key, out var v) ? v : defaultValue;

    public string GetSectionString(string section, string key, string defaultValue) =>
        _sections.TryGetValue(section, out var sec) && sec.TryGetValue(key, out var v) ? v : defaultValue;

    public string? GetSectionStringOrNull(string section, string key) =>
        _sections.TryGetValue(section, out var sec) && sec.TryGetValue(key, out var v) ? v : null;

    public int GetSectionInt(string section, string key, int defaultValue) =>
        _sections.TryGetValue(section, out var sec) && sec.TryGetValue(key, out var v)
            && int.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : defaultValue;

    public double GetSectionDouble(string section, string key, double defaultValue) =>
        _sections.TryGetValue(section, out var sec) && sec.TryGetValue(key, out var v)
            && double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : defaultValue;

    public List<string> GetSectionStringList(string section, string key)
    {
        if (!_sections.TryGetValue(section, out var sec) || !sec.TryGetValue(key, out var v))
            return new List<string>();
        v = v.Trim();
        if (!v.StartsWith('[') || !v.EndsWith(']')) return new List<string>();
        v = v[1..^1];
        return v.Split(',')
            .Select(x => x.Trim().Trim('"').Trim('\''))
            .Where(x => !string.IsNullOrEmpty(x))
            .ToList();
    }

    public List<ProviderEntry> ParseProviders()
    {
        var entries = new List<ProviderEntry>();
        foreach (var p in _providerEntries)
        {
            var entry = new ProviderEntry
            {
                Name = p.GetValueOrDefault("name", ""),
                Kind = p.GetValueOrDefault("kind", "openai"),
                BaseUrl = p.GetValueOrDefault("base_url", ""),
                Model = p.GetValueOrDefault("model"),
                ApiKeyEnv = p.GetValueOrDefault("api_key_env", ""),
                ContextWindow = int.TryParse(p.GetValueOrDefault("context_window", "128000"), out var cw) ? cw : 128000,
                Effort = p.GetValueOrDefault("effort", ""),
            };

            if (p.TryGetValue("models", out var modelsStr))
            {
                var m = modelsStr.Trim();
                if (m.StartsWith('[') && m.EndsWith(']'))
                {
                    var models = m[1..^1].Split(',')
                        .Select(x => x.Trim().Trim('"').Trim('\''))
                        .Where(x => !string.IsNullOrEmpty(x))
                        .ToList();
                    if (models.Count > 0)
                        entry = entry with { Models = models };
                }
            }

            if (p.TryGetValue("price", out var priceStr))
            {
                var inner = priceStr.Trim();
                if (inner.StartsWith('{') && inner.EndsWith('}'))
                {
                    inner = inner[1..^1];
                    var fields = inner.Split(',')
                        .Select(x => x.Split('=', 2))
                        .Where(x => x.Length == 2)
                        .ToDictionary(x => x[0].Trim(), x => x[1].Trim());

                    double Pf(string name) => fields.TryGetValue(name, out var v)
                        && double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 0.0;

                    var currency = fields.TryGetValue("currency", out var curVal) ? curVal.Trim('"') : "\u00a5";

                    entry = entry with
                    {
                        Pricing = new Pricing(Pf("cache_hit"), Pf("input"), Pf("output"), currency)
                    };
                }
            }

            entries.Add(entry);
        }
        return entries;
    }
}
