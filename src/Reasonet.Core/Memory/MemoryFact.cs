namespace Reasonet.Memory;

/// <summary>
/// One stored auto-memory fact.
/// </summary>
public sealed record MemoryFact
{
    /// <summary>Kebab-case slug; also the file stem (&lt;name&gt;.md).</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable index label; falls back to de-kebabed Name.</summary>
    public string? Title { get; init; }

    /// <summary>One-line summary for the index.</summary>
    public required string Description { get; init; }

    /// <summary>Category of the fact.</summary>
    public MemoryType Type { get; init; } = MemoryType.Project;

    /// <summary>The fact itself (Markdown).</summary>
    public required string Body { get; init; }
}
