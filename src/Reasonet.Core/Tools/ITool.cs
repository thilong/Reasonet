using System.Text.Json.Nodes;

namespace Reasonet.Tools;

/// <summary>
/// A capability the model can invoke.
/// </summary>
public interface ITool
{
    string Name { get; }
    string Description { get; }
    /// <summary>JSON Schema for the tool's parameters (as a parsed JsonNode).</summary>
    JsonNode Schema { get; }
    /// <summary>true if no observable side effects on the host.</summary>
    bool IsReadOnly { get; }
    /// <summary>
    /// Execute the tool with the given JSON arguments. Returns result text.
    /// </summary>
    Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default);
}

/// <summary>
/// Optional capability: preview the change a writer tool would make without touching disk.
/// </summary>
public interface IChangePreviewer
{
    (string Diff, int Added, int Removed)? Preview(string argumentsJson);
}
