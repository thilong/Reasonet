namespace Reasonet.Skill;

/// <summary>
/// Options for configuring a SkillStore.
/// </summary>
public sealed record SkillOptions
{
    /// <summary>Project root for project-scoped skill discovery.</summary>
    public string? ProjectRoot { get; init; }

    /// <summary>Extra custom paths for skills.</summary>
    public IReadOnlyList<string> CustomPaths { get; init; } = Array.Empty<string>();

    /// <summary>Skill names to hide from the index and invocation.</summary>
    public IReadOnlyList<string> DisabledNames { get; init; } = Array.Empty<string>();

    /// <summary>Suppress built-in skills (test-only).</summary>
    public bool DisableBuiltins { get; init; }
}

/// <summary>
/// A discovered skill root with its status.
/// </summary>
public sealed record SkillRoot
{
    public string Directory { get; init; } = "";
    public Scope Scope { get; init; }
    public int Priority { get; init; }
    public string Status { get; init; } = "missing";
}

/// <summary>
/// Resolves skills across configured roots: .reasonet/skills under project root
/// → custom paths → built-in skills.
/// Only names + descriptions enter the system-prompt index; bodies load on demand.
/// </summary>
public sealed class SkillStore
{
    private const string SkillsDir = "skills";
    private const string SkillFile = "SKILL.md";

    private readonly string? _projectRoot;
    private readonly List<string> _customPaths;
    private readonly HashSet<string> _disabled;
    private readonly bool _disableBuiltins;

    /// <summary>The convention directories to scan under each root.</summary>
    public static readonly IReadOnlyList<string> ConventionDirs = new[] { ".reasonet", ".agents", ".agent", ".claude" };

    public SkillStore(SkillOptions opts)
    {
        _projectRoot = opts.ProjectRoot != null ? Path.GetFullPath(opts.ProjectRoot) : null;
        _customPaths = opts.CustomPaths.Select(ExpandPath).ToList();
        _disabled = new HashSet<string>(opts.DisabledNames.Select(n => n.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
        _disableBuiltins = opts.DisableBuiltins;
    }

    private static string ExpandPath(string path)
    {
        if (path.StartsWith("~/"))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        return Path.GetFullPath(path);
    }

    /// <summary>Return the discovery roots.</summary>
    public List<SkillRoot> Roots()
    {
        var roots = new List<SkillRoot>();
        int pri = 0;

        // Project convention dirs
        if (_projectRoot != null)
        {
            foreach (var cd in ConventionDirs)
            {
                var dir = Path.Combine(_projectRoot, cd, SkillsDir);
                roots.Add(new SkillRoot { Directory = dir, Scope = Scope.Project, Priority = pri++, Status = DirStatus(dir) });
            }
        }

        // Custom paths
        foreach (var p in _customPaths)
            roots.Add(new SkillRoot { Directory = p, Scope = Scope.Custom, Priority = pri++, Status = DirStatus(p) });

        // Global convention dirs (home)
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var cd in ConventionDirs)
        {
            var dir = Path.Combine(home, cd, SkillsDir);
            roots.Add(new SkillRoot { Directory = dir, Scope = Scope.Global, Priority = pri++, Status = DirStatus(dir) });
        }

        return roots;
    }

    private static string DirStatus(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return "missing";
            // Check readable by opening
            Directory.EnumerateFileSystemEntries(dir).GetEnumerator().Dispose();
            return "ok";
        }
        catch { return "unreadable"; }
    }

    /// <summary>List every discoverable skill, deduped by name (builtins last).</summary>
    public List<Skill> List()
    {
        var byName = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in Roots())
        {
            if (r.Status != "ok") continue;
            foreach (var entry in Directory.EnumerateFileSystemEntries(r.Directory))
            {
                var sk = ReadEntry(r.Directory, r.Scope, entry);
                if (sk != null && !_disabled.Contains(sk.Name) && !byName.ContainsKey(sk.Name))
                    byName[sk.Name] = sk;
            }
        }

        // Built-in skills
        if (!_disableBuiltins)
        {
            foreach (var sk in BuiltinSkills.All())
            {
                if (!_disabled.Contains(sk.Name) && !byName.ContainsKey(sk.Name))
                    byName[sk.Name] = sk;
            }
        }

        return byName.Values.OrderBy(s => s.Name).ToList();
    }

    /// <summary>Read one skill by name.</summary>
    public Skill? Read(string name)
    {
        if (string.IsNullOrEmpty(name) || _disabled.Contains(name))
            return null;

        foreach (var r in Roots())
        {
            if (r.Status != "ok") continue;

            // Try directory layout: <dir>/<name>/SKILL.md
            var dirCand = Path.Combine(r.Directory, name, SkillFile);
            if (File.Exists(dirCand))
                return Parse(dirCand, name, r.Scope);

            // Try flat file: <dir>/<name>.md
            var flatCand = Path.Combine(r.Directory, name + ".md");
            if (File.Exists(flatCand))
                return Parse(flatCand, name, r.Scope);
        }

        // Built-ins
        if (!_disableBuiltins)
        {
            foreach (var sk in BuiltinSkills.All())
            {
                if (sk.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return sk;
            }
        }

        return null;
    }

    private Skill? ReadEntry(string dir, Scope scope, string fullPath)
    {
        var name = Path.GetFileName(fullPath);
        bool isDir = Directory.Exists(fullPath);
        bool isFile = File.Exists(fullPath);

        if (isDir)
        {
            if (!IsValidName(name)) return null;
            var skillPath = Path.Combine(fullPath, SkillFile);
            return File.Exists(skillPath) ? Parse(skillPath, name, scope) : null;
        }
        if (isFile && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            var stem = Path.GetFileNameWithoutExtension(name);
            if (!IsValidName(stem)) return null;
            return Parse(fullPath, stem, scope);
        }
        return null;
    }

    private Skill? Parse(string path, string stem, Scope scope)
    {
        try
        {
            var text = File.ReadAllText(path);
            var (fm, body) = Frontmatter.Split(text);

            var name = stem;
            if (fm.TryGetValue("name", out var fmName) && IsValidName(fmName))
                name = fmName;

            var desc = fm.GetValueOrDefault("description", "").Trim();
            var runAs = ParseRunAs(fm);
            var allowedTools = ParseAllowedTools(fm);
            var model = fm.GetValueOrDefault("model", "");

            return new Skill
            {
                Name = name,
                Description = desc,
                Body = body.Trim(),
                Scope = scope,
                Path = path,
                AllowedTools = allowedTools,
                RunAs = runAs,
                Model = string.IsNullOrWhiteSpace(model) ? null : model,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Create a skill stub at the specified scope root.</summary>
    public (string Path, string? Error) Create(string name, Scope scope)
    {
        var content = $@"---
name: {name}
description: ""
---

# {name}

Describe what this playbook does.
";
        return CreateWithContent(name, scope, content);
    }

    /// <summary>Write a new skill file, refusing to overwrite.</summary>
    public (string Path, string? Error) CreateWithContent(string name, Scope scope, string content)
    {
        if (!IsValidName(name))
            return ("", $"invalid skill name \"{name}\"");

        string root;
        switch (scope)
        {
            case Scope.Project:
                if (_projectRoot == null)
                    return ("", "project scope requires a workspace directory");
                root = Path.Combine(_projectRoot, ".reasonet", SkillsDir);
                break;
            default:
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".reasonet", SkillsDir);
                break;
        }

        var flat = Path.Combine(root, name + ".md");
        var folder = Path.Combine(root, name, SkillFile);

        if (File.Exists(folder))
            return ("", $"skill \"{name}\" already exists at {folder}");
        if (File.Exists(flat))
            return ("", $"skill \"{name}\" already exists at {flat}");

        Directory.CreateDirectory(root);
        File.WriteAllText(flat, content);
        return (flat, null);
    }

    #region Parsing helpers

    private static RunAs ParseRunAs(Dictionary<string, string> fm)
    {
        // Check legacy aliases: context, agent -> runas
        var raw = fm.GetValueOrDefault("runas", "")
                  ?? fm.GetValueOrDefault("context", "")
                  ?? fm.GetValueOrDefault("agent", "");

        return raw.Trim().ToLowerInvariant() switch
        {
            "subagent" => RunAs.Subagent,
            _ => RunAs.Inline,
        };
    }

    private static List<string> ParseAllowedTools(Dictionary<string, string> fm)
    {
        if (!fm.TryGetValue("allowed-tools", out var raw) || string.IsNullOrWhiteSpace(raw))
            return new List<string>();
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    /// <summary>Valid skill identifier: alphanumeric, dots, underscores, hyphens.</summary>
    public static bool IsValidName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-');
    }

    #endregion
}
