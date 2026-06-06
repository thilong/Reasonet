namespace Reasonet.Memory;

/// <summary>
/// Where a memory doc was discovered. Ascending precedence order.
/// </summary>
public enum DocScope
{
    /// <summary>Project scope: &lt;workspace&gt;/.reasonet/AGENTS.md</summary>
    Project,
    /// <summary>Local override: &lt;workspace&gt;/.reasonet/AGENTS.local.md</summary>
    Local,
}

/// <summary>
/// Classification for auto-memory facts.
/// </summary>
public enum MemoryType
{
    User,      // who the user is: role, preferences, expertise
    Feedback,  // guidance on how to work (with why + how-to-apply)
    Project,   // ongoing work / goals / constraints not in the code
    Reference, // pointers to external resources (URLs, tickets)
}
