namespace Reasonet.Tools;

/// <summary>
/// Filter a tool registry, excluding meta-tools that spawn or author agent work.
/// </summary>
public static class ToolFilter
{
    public static readonly IReadOnlyList<string> MetaTools = new[]
    {
        "task", "run_skill", "install_skill",
        "explore", "research", "review", "security_review"
    };

    /// <summary>
    /// Build a sub-registry from a parent, optionally filtering to named tools
    /// and excluding meta-tools.
    /// </summary>
    public static ToolRegistry Filter(ToolRegistry parent, IReadOnlyList<string>? names, IReadOnlyList<string>? exclude = null)
    {
        var ex = new HashSet<string>(exclude ?? MetaTools, StringComparer.Ordinal);
        var sub = new ToolRegistry();
        var src = names ?? parent.Names();
        foreach (var name in src)
        {
            if (ex.Contains(name)) continue;
            var t = parent.Get(name);
            if (t != null) sub.Add(t);
        }
        return sub;
    }
}
