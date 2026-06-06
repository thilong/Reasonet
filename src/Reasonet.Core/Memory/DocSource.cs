namespace Reasonet.Memory;

/// <summary>
/// One loaded memory document with provenance.
/// </summary>
public sealed record DocSource
{
    public required string Path { get; init; }
    public DocScope Scope { get; init; }
    public required string Body { get; init; }
}
