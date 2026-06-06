using System.Text.Json.Serialization;

namespace Reasonet.Messages;

/// <summary>
/// A tool invocation requested by the model.
/// </summary>
public sealed record ToolCall
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("arguments")]
    public required string Arguments { get; init; }
}
