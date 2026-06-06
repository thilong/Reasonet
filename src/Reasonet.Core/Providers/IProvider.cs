namespace Reasonet.Providers;

/// <summary>
/// Provider is a chat-capable model backend.
/// </summary>
public interface IProvider
{
    /// <summary>
    /// Provider instance name, e.g. "deepseek" / "mimo".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Start a streaming completion. Cancelling the context must abort the request.
    /// </summary>
    IAsyncEnumerable<StreamChunk> StreamAsync(
        ProviderRequest request,
        CancellationToken ct = default
    );
}
