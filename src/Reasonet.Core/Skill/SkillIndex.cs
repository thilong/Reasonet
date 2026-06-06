using System.Text;

namespace Reasonet.Skill;

/// <summary>
/// Generates the skills index that's folded into the system prompt.
/// Only names + descriptions (+ a subagent tag) are listed; bodies load on demand.
/// </summary>
public static class SkillIndex
{
    /// <summary>Max chars of the skills index to prevent prompt bloat.</summary>
    private const int IndexMaxChars = 4000;

    private const string IndexHeader = """
        # Skills — playbooks you can invoke

        One-liner index. Before non-trivial work, scan it: if an untagged (inline) skill is even plausibly relevant to the task, invoke it before continuing instead of pre-judging — loading one imperfect inline skill is cheap. Skills tagged `[subagent]` are the heavy path; reach for them only when the task genuinely needs context-heavy work, not on weak relevance. Each entry is a built-in or a user-authored playbook. Call `run_skill({ name: "<skill-name>", arguments: "<task>" })`. Entries tagged `[subagent]` spawn an isolated subagent — its tool calls and reasoning never enter your context, only its final answer does. Untagged skills are inlined: the body becomes a tool result you read and act on directly. The user can also invoke a skill via `/<name>`.
        """;

    /// <summary>
    /// Append the skills index to basePrompt, or return unchanged when no skills.
    /// </summary>
    public static string Apply(string basePrompt, IReadOnlyList<Skill> skills)
    {
        if (skills.Count == 0)
            return basePrompt;

        var sb = new StringBuilder();
        foreach (var sk in skills)
        {
            sb.AppendLine(IndexLine(sk));
        }

        var joined = sb.ToString().TrimEnd();
        if (joined.Length > IndexMaxChars)
            joined = joined[..IndexMaxChars] + $"\n… (truncated)";

        return basePrompt + "\n\n" + IndexHeader + "\n\n```\n" + joined + "\n```";
    }

    /// <summary>
    /// Render one skill as "- name [tag] — description".
    /// </summary>
    private static string IndexLine(Skill sk)
    {
        var desc = sk.Description.Replace('\n', ' ').Trim();
        if (string.IsNullOrEmpty(desc))
            desc = "(no description)";

        var tag = sk.RunAs == RunAs.Subagent ? " [subagent]" : "";

        // Clip description to keep line readable (~120 chars)
        var max = 120 - sk.Name.Length - tag.Length;
        if (max < 1) max = 1;
        var clipped = desc.Length <= max ? desc : desc[..(max - 1)] + "\u2026";

        return $"- {sk.Name}{tag} — {clipped}";
    }
}
