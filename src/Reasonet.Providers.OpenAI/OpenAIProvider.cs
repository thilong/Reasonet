using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Reasonet.Messages;
using Reasonet.Providers;

namespace Reasonet.Providers.OpenAI;

/// <summary>
/// OpenAI-compatible chat completions provider (works with DeepSeek, MiMo, etc.).
/// </summary>
public sealed class OpenAIProvider : IProvider
{
    private readonly string _name;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly bool _isDeepSeek;
    private readonly string? _effort;
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAIProvider(
        string name,
        string apiKey,
        string baseUrl,
        string model,
        bool isDeepSeek = false,
        string? effort = null,
        HttpClient? http = null)
    {
        _name = name;
        _apiKey = apiKey;
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _isDeepSeek = isDeepSeek;
        _effort = effort;
        _http = http ?? new HttpClient();
    }

    public string Name => _name;

    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        ProviderRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Build the wire request
        var wireReq = BuildRequest(request);

        // Serialize once
        var json = JsonSerializer.Serialize(wireReq, JsonOptions);
        var body = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = body
        };
        httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        httpReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _http.SendAsync(
            httpReq, HttpCompletionOption.ResponseHeadersRead, ct);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        // Tool call accumulators
        var toolAcc = new Dictionary<int, (string Id, string Name, StringBuilder Args)>();
        var toolOrder = new List<int>();
        var toolStarted = new HashSet<int>();
        var lastFinishReason = "";

        string? readLine;
        while ((readLine = await reader.ReadLineAsync(ct)) != null)
        {
            ct.ThrowIfCancellationRequested();
            var line = readLine;

            var trimmed = line.Trim();
            if (trimmed == "" || !trimmed.StartsWith("data:")) continue;

            var data = trimmed["data:".Length..].Trim();
            if (data == "[DONE]") break;

            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            // Error
            if (root.TryGetProperty("error", out var errEl))
            {
                var msg = errEl.TryGetProperty("message", out var m) ? m.GetString() : "unknown error";
                yield return new StreamChunk(ChunkType.Error, Error: new Exception(msg));
                yield break;
            }

            // Usage - may be null in some SSE chunks
            if (root.TryGetProperty("usage", out var usageEl) && usageEl.ValueKind == JsonValueKind.Object)
            {
                var u = ParseUsage(usageEl, lastFinishReason);
                yield return new StreamChunk(ChunkType.Usage, Usage: u);
            }

            // Choices - skip empty, null, or non-object chunks (usage-only keep-alive chunks)
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                continue;

            var choice = choices[0];
            if (choice.ValueKind != JsonValueKind.Object)
                continue;

            // Finish reason
            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
            {
                var frStr = fr.GetString()!;
                if (!string.IsNullOrEmpty(frStr))
                    lastFinishReason = frStr;
            }

            // Delta may exist as null in usage-only chunks
            if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
                continue;

            // Reasoning content
            if (delta.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
            {
                var txt = rc.GetString() ?? "";
                if (!string.IsNullOrEmpty(txt))
                {
                    yield return new StreamChunk(ChunkType.Reasoning, Text: txt);
                }
            }

            // Text content
            if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                var txt = content.GetString() ?? "";
                if (!string.IsNullOrEmpty(txt))
                {
                    yield return new StreamChunk(ChunkType.Text, Text: txt);
                }
            }

            // Tool calls (streaming)
            if (!delta.TryGetProperty("tool_calls", out var tcArray)) continue;

            foreach (var tc in tcArray.EnumerateArray())
            {
                var idx = tc.TryGetProperty("index", out var idxEl) ? idxEl.GetInt32() : 0;

                if (!toolAcc.ContainsKey(idx))
                {
                    toolAcc[idx] = ("", "", new StringBuilder());
                    toolOrder.Add(idx);
                }

                var (id, name, argsSb) = toolAcc[idx];

                if (tc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    id = idEl.GetString() ?? id;

                if (tc.TryGetProperty("function", out var fn))
                {
                    if (fn.TryGetProperty("name", out var fnName) && fnName.ValueKind == JsonValueKind.String)
                        name = fnName.GetString() ?? name;
                    if (fn.TryGetProperty("arguments", out var fnArgs) && fnArgs.ValueKind == JsonValueKind.String)
                        argsSb.Append(fnArgs.GetString());
                }

                toolAcc[idx] = (id, name, argsSb);

                // Emit ToolCallStart as soon as name is known
                if (!toolStarted.Contains(idx) && !string.IsNullOrEmpty(name))
                {
                    toolStarted.Add(idx);
                    yield return new StreamChunk(ChunkType.ToolCallStart,
                        ToolCall: new ToolCallInfo(idx, Id: id, Name: name));
                }
            }
        }

        // Emit complete tool calls in order
        foreach (var idx in toolOrder)
        {
            var (id, name, argsSb) = toolAcc[idx];
            if (string.IsNullOrEmpty(id))
                id = $"call_{idx}";
            yield return new StreamChunk(ChunkType.ToolCall,
                ToolCall: new ToolCallInfo(idx, id, name, argsSb.ToString()));
        }

        yield return new StreamChunk(ChunkType.Done);
    }

    private WireRequest BuildRequest(ProviderRequest req)
    {
        var msgs = SanitizeToolPairing(req.Messages);
        var wireMsgs = msgs.Select(m => new WireMessage
        {
            Role = m.Role switch
            {
                Role.System => "system",
                Role.User => "user",
                Role.Assistant => "assistant",
                Role.Tool => "tool",
                _ => "user"
            },
            Content = m.Content ?? "",
            ToolCallId = m.ToolCallId,
            Name = m.Name,
            ToolCalls = m.ToolCalls?.Select(tc => new WireToolCall
            {
                Id = tc.Id,
                Type = "function",
                Function = new WireFunction { Name = tc.Name, Arguments = tc.Arguments }
            }).ToList()
        }).ToList();

        var tools = req.Tools.Select(t => new WireTool
        {
            Type = "function",
            Function = new WireToolFunction
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.Parameters
            }
        }).ToList();

        var wire = new WireRequest
        {
            Model = _model,
            Messages = wireMsgs,
            Tools = tools.Count > 0 ? tools : null,
            Stream = true,
            StreamOptions = new WireStreamOptions { IncludeUsage = true },
            Temperature = req.Temperature,
            MaxTokens = req.MaxTokens > 0 ? req.MaxTokens : null,
            ReasoningEffort = _effort
        };

        if (_isDeepSeek)
            wire = wire with { Thinking = new WireThinking { Type = "enabled" } };

        return wire;
    }

    private static Usage ParseUsage(JsonElement el, string finishReason)
    {
        var promptTokens = el.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
        var completionTokens = el.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0;
        var totalTokens = el.TryGetProperty("total_tokens", out var tt) ? tt.GetInt32() : 0;

        // DeepSeek format
        var cacheHit = el.TryGetProperty("prompt_cache_hit_tokens", out var pcht) ? pcht.GetInt32() : 0;
        var cacheMiss = el.TryGetProperty("prompt_cache_miss_tokens", out var pcmt) ? pcmt.GetInt32() : 0;

        // OpenAI/MiMo format
        if (cacheHit == 0 && el.TryGetProperty("prompt_tokens_details", out var ptd))
        {
            if (ptd.TryGetProperty("cached_tokens", out var cached))
                cacheHit = cached.GetInt32();
        }
        if (cacheMiss == 0 && cacheHit > 0 && promptTokens > cacheHit)
            cacheMiss = promptTokens - cacheHit;

        var reasoning = 0;
        if (el.TryGetProperty("completion_tokens_details", out var ctd))
        {
            if (ctd.TryGetProperty("reasoning_tokens", out var rt))
                reasoning = rt.GetInt32();
        }

        return new Usage(promptTokens, completionTokens, totalTokens, cacheHit, cacheMiss, reasoning, finishReason);
    }

    /// <summary>
    /// Repair tool-call pairing: backfill interrupted tool calls so the API doesn't reject.
    /// </summary>
    internal static IReadOnlyList<Message> SanitizeToolPairing(IReadOnlyList<Message> msgs)
    {
        var result = new List<Message>(msgs.Count);
        for (int i = 0; i < msgs.Count; i++)
        {
            var m = msgs[i];
            if (m.Role == Role.Assistant && m.ToolCalls is { Count: > 0 })
            {
                result.Add(m);
                int j = i + 1;
                while (j < msgs.Count && msgs[j].Role == Role.Tool)
                    j++;

                var toolResults = new List<Message>();
                for (int k = i + 1; k < j; k++)
                    toolResults.Add(msgs[k]);

                foreach (var tc in m.ToolCalls)
                {
                    Message? match = null;
                    foreach (var r in toolResults)
                    {
                        if (r.ToolCallId == tc.Id)
                        {
                            match = r;
                            break;
                        }
                    }
                    result.Add(match ?? new Message
                    {
                        Role = Role.Tool,
                        ToolCallId = tc.Id,
                        Name = tc.Name,
                        Content = "[no result: interrupted]"
                    });
                }
                i = j - 1;
                continue;
            }
            if (m.Role == Role.Tool) continue; // drop orphan tool messages
            result.Add(m);
        }
        return result.AsReadOnly();
    }

    #region Wire Protocol Types (JSON)

    private sealed record WireRequest
    {
        [JsonPropertyName("model")] public string Model { get; init; } = "";
        [JsonPropertyName("messages")] public List<WireMessage> Messages { get; init; } = [];
        [JsonPropertyName("tools")] public List<WireTool>? Tools { get; init; }
        [JsonPropertyName("stream")] public bool Stream { get; init; }
        [JsonPropertyName("stream_options")] public WireStreamOptions? StreamOptions { get; init; }
        [JsonPropertyName("temperature")] public double Temperature { get; init; }
        [JsonPropertyName("max_tokens")] public int? MaxTokens { get; init; }
        [JsonPropertyName("reasoning_effort")] public string? ReasoningEffort { get; init; }
        [JsonPropertyName("thinking")] public WireThinking? Thinking { get; init; }
    }

    private sealed record WireStreamOptions
    {
        [JsonPropertyName("include_usage")] public bool IncludeUsage { get; init; }
    }

    private sealed record WireThinking
    {
        [JsonPropertyName("type")] public string Type { get; init; } = "enabled";
    }

    private sealed record WireMessage
    {
        [JsonPropertyName("role")] public string Role { get; init; } = "";
        [JsonPropertyName("content")] public string Content { get; init; } = "";
        [JsonPropertyName("tool_calls")] public List<WireToolCall>? ToolCalls { get; init; }
        [JsonPropertyName("tool_call_id")] public string? ToolCallId { get; init; }
        [JsonPropertyName("name")] public string? Name { get; init; }
    }

    private sealed record WireTool
    {
        [JsonPropertyName("type")] public string Type { get; init; } = "function";
        [JsonPropertyName("function")] public WireToolFunction Function { get; init; } = new();
    }

    private sealed record WireToolFunction
    {
        [JsonPropertyName("name")] public string Name { get; init; } = "";
        [JsonPropertyName("description")] public string Description { get; init; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("parameters")] public JsonNode? Parameters { get; init; }
    }

    private sealed record WireToolCall
    {
        [JsonPropertyName("id")] public string Id { get; init; } = "";
        [JsonPropertyName("type")] public string Type { get; init; } = "function";
        [JsonPropertyName("function")] public WireFunction Function { get; init; } = new();
    }

    private sealed record WireFunction
    {
        [JsonPropertyName("name")] public string Name { get; init; } = "";
        [JsonPropertyName("arguments")] public string Arguments { get; init; } = "";
    }

    #endregion
}
