using Reasonet.Providers;

namespace Reasonet.Configuration;

/// <summary>
/// Reasonet runtime configuration, loaded from TOML.
/// Resolution: flag > ./reasonix.toml > ~/.config/reasonix/config.toml > defaults.
/// </summary>
public sealed record ReasonetConfig
{
    public string DefaultModel { get; init; } = "";
    public string Language { get; init; } = "";
    public AgentConfig Agent { get; init; } = new();
    public List<ProviderEntry> Providers { get; init; } = [];
    public ToolsConfig Tools { get; init; } = new();
    public PermissionsConfig Permissions { get; init; } = new();
    public NetworkConfig Network { get; init; } = new();
    public SkillsConfig? Skills { get; init; }
}

public sealed record AgentConfig
{
    public string SystemPrompt { get; init; } = DefaultSystemPrompt;
    public int MaxSteps { get; init; } = 25;
    public double Temperature { get; init; } = 0.0;
    public string AutoPlan { get; init; } = "off";
    public double SoftCompactRatio { get; init; } = 0.5;
    public double CompactRatio { get; init; } = 0.8;
    public double CompactForceRatio { get; init; } = 0.9;
    public string? PlannerModel { get; init; }

    public const string DefaultSystemPrompt = """
        You are Reasonet, a coding agent focused on executing code tasks.
        Use the provided tools to read and write files and run shell commands.
        Principles: understand the request before acting; verify with tools instead of
        guessing; keep changes minimal and correct; briefly summarize what you did.
        """;
}

public sealed record ProviderEntry
{
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "openai";
    public string BaseUrl { get; init; } = "";
    public List<string> Models { get; init; } = [];
    public string? Model { get; init; }
    public string ApiKeyEnv { get; init; } = "";
    public int ContextWindow { get; init; } = 128_000;
    public Pricing? Pricing { get; init; }
    public string Effort { get; init; } = "";
}

public sealed record ToolsConfig
{
    public List<string> Enabled { get; init; } = [];
}

public sealed record PermissionsConfig
{
    public string Mode { get; init; } = "allow";
    public List<string> Allow { get; init; } = [];
    public List<string> Ask { get; init; } = [];
    public List<string> Deny { get; init; } = [];
}

public sealed record NetworkConfig
{
    public string? Proxy { get; init; }
    public bool NoProxy { get; init; }
}

public sealed record SkillsConfig
{
    public List<string> Paths { get; init; } = [];
    public List<string> DisabledSkills { get; init; } = [];
}
