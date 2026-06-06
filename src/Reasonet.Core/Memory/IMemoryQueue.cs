namespace Reasonet.Memory;

/// <summary>
/// Receives a one-line note about a memory change just made, so the controller
/// can fold it into the current turn — taking effect this session without touching
/// the cache-stable system prefix.
/// </summary>
public interface IMemoryQueue
{
    void Enqueue(string note);
}
