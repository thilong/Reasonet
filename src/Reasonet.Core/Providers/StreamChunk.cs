namespace Reasonet.Providers;

/// <summary>
/// Type of a streamed chunk from the LLM provider.
/// </summary>
public enum ChunkType
{
    Text,           // Text delta
    Reasoning,      // Thinking-mode reasoning delta
    ToolCallStart,  // Tool call has begun (ID + Name known)
    ToolCall,       // One complete tool call
    Usage,          // Token usage
    Done,           // Completion finished normally
    Error           // An error occurred
}

/// <summary>
/// A single streamed chunk from an LLM provider.
/// </summary>
public sealed record StreamChunk(
    ChunkType Type,
    string? Text = null,
    string? Signature = null,
    ToolCallInfo? ToolCall = null,
    Usage? Usage = null,
    Exception? Error = null
);

/// <summary>
/// Partial or complete tool call info carried in a stream chunk.
/// </summary>
public sealed record ToolCallInfo(
    int Index,
    string? Id = null,
    string? Name = null,
    string? Arguments = null
);
