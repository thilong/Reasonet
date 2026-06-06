using System.Text.Json.Nodes;
using Reasonet.Messages;

namespace Reasonet.Providers;

/// <summary>
/// Per-turn token usage telemetry.
/// </summary>
public sealed record Usage(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    int CacheHitTokens,
    int CacheMissTokens,
    int ReasoningTokens,
    string? FinishReason
);

/// <summary>
/// Per-1M-token pricing rates.
/// </summary>
public sealed record Pricing(
    double CacheHit,
    double Input,
    double Output,
    string Currency = "\u00a5"
)
{
    public double Cost(Usage u) =>
        (u.CacheHitTokens * CacheHit +
         u.CacheMissTokens * Input +
         u.CompletionTokens * Output) / 1_000_000.0;
}

/// <summary>
/// A single completion request.
/// </summary>
public sealed record ProviderRequest(
    IReadOnlyList<Message> Messages,
    IReadOnlyList<ToolSchema> Tools,
    double Temperature,
    int MaxTokens = 0
);

/// <summary>
/// Tool schema exposed to the model (JSON Schema format).
/// </summary>
public sealed record ToolSchema(
    string Name,
    string Description,
    JsonNode Parameters
);
