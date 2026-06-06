using System.Text.Json.Serialization;

namespace Reasonet.Messages;

/// <summary>
/// A single conversation message.
/// </summary>
public sealed record Message
{
    [JsonPropertyName("role")]
    public required Role Role { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>
    /// Thinking-mode chain-of-thought (assistant only). Not re-uploaded to the API.
    /// </summary>
    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; init; }

    /// <summary>
    /// Opaque proof for reasoning (Anthropic extended thinking).
    /// </summary>
    [JsonPropertyName("reasoning_signature")]
    public string? ReasoningSignature { get; init; }

    /// <summary>
    /// Tool calls requested by the model (assistant messages).
    /// </summary>
    [JsonPropertyName("tool_calls")]
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }

    /// <summary>
    /// Links a tool result to its call (tool messages).
    /// </summary>
    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; init; }

    /// <summary>
    /// Tool name (tool messages).
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
