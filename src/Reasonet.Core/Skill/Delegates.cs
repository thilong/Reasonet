namespace Reasonet.Skill;

/// <summary>
/// Delegate for running a subagent skill: spawns an isolated child loop with
/// the skill body as system prompt and returns only the final answer.
/// </summary>
public delegate Task<string> SubagentRunner(Skill skill, string task, CancellationToken ct = default);

/// <summary>
/// Delegate called after a skill is installed, so UI can refresh.
/// </summary>
public delegate void InstalledHook(string name, string path, Scope scope);
