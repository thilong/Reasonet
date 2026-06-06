namespace Reasonet.Skill;

/// <summary>
/// Built-in skills that ship with Reasonet.
/// A user/project file with the same name overrides these.
/// </summary>
public static class BuiltinSkills
{
    private const string NegClaimRule =
        "When you claim something does NOT exist, say which searches you ran.";
    private const string TuiFmt =
        "Keep the final answer compact and terminal-friendly.";

    public static IReadOnlyList<Skill> All() => _skills.Value;
    private static readonly Lazy<List<Skill>> _skills = new(() => new()
    {
        Init,
        Explore,
        Research,
        Review,
        SecurityReview,
        Test,
    });

    private static readonly string[] ReadTools = { "read_file", "glob", "grep" };
    private static readonly string[] ReviewTools = { "read_file", "glob", "grep", "bash" };

    public static readonly Skill Init = new()
    {
        Name = "init",
        Description = "Bootstrap or refresh AGENTS.md — analyze codebase structure, build/test commands, architecture, conventions. Inlined — runs in the main loop.",
        Body = InitBody,
        Scope = Scope.Builtin,
        Path = "(builtin)",
        RunAs = RunAs.Inline,
    };

    public static readonly Skill Explore = new()
    {
        Name = "explore",
        Description = "Explore the codebase in an isolated subagent — wide-net read-only investigation returning one distilled answer.",
        Body = ExploreBody,
        Scope = Scope.Builtin,
        Path = "(builtin)",
        RunAs = RunAs.Subagent,
        AllowedTools = ReadTools,
    };

    public static readonly Skill Research = new()
    {
        Name = "research",
        Description = "Research a question combining web fetch + code reading in an isolated subagent.",
        Body = ResearchBody,
        Scope = Scope.Builtin,
        Path = "(builtin)",
        RunAs = RunAs.Subagent,
        AllowedTools = ReadTools.Append("web_fetch").ToArray(),
    };

    public static readonly Skill Review = new()
    {
        Name = "review",
        Description = "Review pending changes (current branch diff) in an isolated subagent — flags correctness, security, missing tests.",
        Body = ReviewBody,
        Scope = Scope.Builtin,
        Path = "(builtin)",
        RunAs = RunAs.Subagent,
        AllowedTools = ReviewTools,
    };

    public static readonly Skill SecurityReview = new()
    {
        Name = "security-review",
        Description = "Security-focused review of current branch diff in an isolated subagent — injection, authz, secrets, crypto issues. Read-only.",
        Body = SecurityBody,
        Scope = Scope.Builtin,
        Path = "(builtin)",
        RunAs = RunAs.Subagent,
        AllowedTools = ReviewTools,
    };

    public static readonly Skill Test = new()
    {
        Name = "test",
        Description = "Run tests, diagnose failures, fix, re-run until green. Inlined — runs in the main loop so you see and approve changes.",
        Body = TestBody,
        Scope = Scope.Builtin,
        Path = "(builtin)",
        RunAs = RunAs.Inline,
    };

    #region Bodies

    private const string InitBody = """
This skill is INLINED — you run in the parent loop. The user invoked /init: bootstrap (or refresh) this project's AGENTS.md. Analyze the codebase, then write a concise, high-signal AGENTS.md.

How to operate:
1. Check for existing AGENTS.md / REASONIX.md / CLAUDE.md. If one exists, read it and IMPROVE it in place.
2. Explore enough to be accurate: project shape, manifest, README, build/test/run commands, architecture, conventions.
3. Write AGENTS.md with write_file. Each section terse: ## Project, ## Commands, ## Architecture, ## Conventions, ## Notes.
4. Keep it tight — every line costs context. Prefer specifics (file paths, command names) over prose.

Rules: Verify commands before writing. Don't fabricate conventions. Never include secrets.
""";

    private const string ExploreBody = $"""
You are running as an exploration subagent. Investigate the codebase the parent pointed you at, then return one focused, distilled answer.

How to operate:
- Use read_file, grep, glob, ls as your primary tools. Stay read-only.
- Cast a wide net first (grep for symbol references, ls/glob for structure), then read the 3-10 most relevant files.
- Stop exploring as soon as you can answer.

Your final answer:
- One paragraph (or a few short bullets). Lead with the conclusion.
- Cite specific file paths + line ranges when they support the answer.

{NegClaimRule}

{TuiFmt}
""";

    private const string ResearchBody = $"""
You are running as a research subagent. Gather information from code AND the web, synthesize it, and return one focused conclusion.

How to operate:
- Combine code reading (read_file, grep, glob) with web_fetch as appropriate.
- Cap yourself at ~10 tool calls.

Your final answer:
- One paragraph (or short bullets). Lead with the conclusion.
- Cite both code (file:line) AND web sources (URL).
- Distinguish "I verified this in code" from "I read this on a docs page".

{NegClaimRule}

{TuiFmt}
""";

    private const string ReviewBody = $"""
You are running as a code-review subagent. Inspect the current branch's diff and produce a focused review.

How to operate:
- Discover scope first: bash git status, git diff --stat, git log --oneline. Then git diff for the hunks.
- Read touched files when the diff lacks context.
- Stay read-only. Never commit, never write files.

What to look for:
1. Correctness bugs. 2. Security. 3. Behavior changes the diff hides. 4. Tests. 5. Style + consistency.

Your final answer:
- Lead with a one-sentence verdict.
- Short bulleted list, each with file:line + problem + what to change.
- If clean, say so. Don't manufacture concerns.

{NegClaimRule}

{TuiFmt}
""";

    private const string SecurityBody = $"""
You are running as a security-review subagent. Inspect the current branch's diff through a security lens.

How to operate:
- Discover scope first: git status, git diff --stat, git diff.
- Read touched files when the diff lacks security context.
- Stay read-only. Never write, never run destructive commands.

Threat model — flag with severity:
CRITICAL: injection, path traversal, missing authn/authz, hardcoded secrets, crypto mistakes.
HIGH: XSS, SSRF, TOCTOU, open redirects.
MEDIUM: verbose errors leaking internals, missing cookie flags.

Your final answer: verdict + severity-grouped items with file:line + threat + fix direction.

{NegClaimRule}

{TuiFmt}
""";

    private const string TestBody = """
This skill is INLINED. Run the project's test suite, diagnose failures, fix, re-run. Repeat until green.

How to operate:
1. Detect the test command: go test, npm test, pytest, cargo test, etc.
2. Run it via bash (timeout ~120s). Capture stdout + stderr.
3. Read failures: which tests, the error, file + line.
4. Fix each failure: production bug → fix code; test bug → fix test.
5. Apply edit and re-run. Iterate.
6. Stop: all green → report; same failure after 2 attempts → STOP and explain.

Don't: install/update deps without asking; skip/disable failing tests; edit test runner config.
""";

    #endregion
}
