namespace Reasonet.Agent;

/// <summary>
/// Interface for spawning sub-agents. The TaskTool depends on this so it
/// doesn't need a direct reference to Reasonet.Agent.
/// </summary>
public interface ISubAgentRunner
{
    /// <summary>
    /// Run a sub-agent with the given prompt and tool set.
    /// Returns the sub-agent's final assistant answer.
    /// </summary>
    Task<string> RunSubAgentAsync(string prompt, Tools.ToolRegistry tools, int maxSteps, CancellationToken ct = default);

    /// <summary>
    /// Return the parent tool registry for tool filtering.
    /// </summary>
    Tools.ToolRegistry GetToolRegistry();
}
