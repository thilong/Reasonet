using System.Text.Json.Nodes;
using Reasonet.Providers;

namespace Reasonet.Tools;

/// <summary>
/// Thread-safe per-run registry of tools.
/// </summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.Ordinal);

    public void Add(ITool tool)
    {
        lock (_tools)
            _tools[tool.Name] = tool;
    }

    public ITool? Get(string name)
    {
        lock (_tools)
            return _tools.GetValueOrDefault(name);
    }

    public IReadOnlyList<ITool> ListAll()
    {
        lock (_tools)
            return _tools.Values.ToList().AsReadOnly();
    }

    public IReadOnlyList<ITool> ListAllSorted()
    {
        lock (_tools)
            return _tools.Values.OrderBy(t => t.Name).ToList().AsReadOnly();
    }

    public IReadOnlyList<ToolSchema> Schemas()
    {
        lock (_tools)
            return _tools.Values
                .OrderBy(t => t.Name)
                .Select(t => new ToolSchema(t.Name, t.Description, t.Schema))
                .ToList()
                .AsReadOnly();
    }

    public IReadOnlyList<string> Names()
    {
        lock (_tools)
            return _tools.Keys.ToList().AsReadOnly();
    }
}
