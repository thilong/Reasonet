namespace Reasonet.Skill;

/// <summary>
/// Where a skill was loaded from. Higher-priority wins on name collision.
/// </summary>
public enum Scope
{
    Builtin,
    Project,
    Custom,
    Global
}

/// <summary>
/// How an invoked skill executes.
/// </summary>
public enum RunAs
{
    Inline,
    Subagent
}

/// <summary>
/// A loaded playbook — an invokable prompt body the model can run.
/// </summary>
public sealed record Skill
{
    /// <summary>Canonical identifier; matches directory / filename stem.</summary>
    public required string Name { get; init; }

    /// <summary>One-liner shown in the pinned system-prompt index.</summary>
    public required string Description { get; init; }

    /// <summary>Full markdown body (post-frontmatter).</summary>
    public required string Body { get; init; }

    /// <summary>Where it came from.</summary>
    public Scope Scope { get; init; }

    /// <summary>Absolute path to the SKILL.md / &lt;name&gt;.md, or "(builtin)".</summary>
    public string Path { get; init; } = "";

    /// <summary>When non-empty, scopes a subagent skill's allowed tools.</summary>
    public IReadOnlyList<string> AllowedTools { get; init; } = Array.Empty<string>();

    /// <summary>inline | subagent</summary>
    public RunAs RunAs { get; init; } = RunAs.Inline;

    /// <summary>Optional model override for runAs=subagent.</summary>
    public string? Model { get; init; }
}
