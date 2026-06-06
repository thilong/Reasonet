using System.Text;
using System.Text.RegularExpressions;

namespace Reasonet.Memory;

/// <summary>
/// Per-project auto-memory store: a directory of one-fact-per-file Markdown notes
/// with frontmatter, plus a MEMORY.md index.
/// Lives under &lt;workspace&gt;/.reasonet/memory/.
/// </summary>
public sealed class MemoryStore
{
    private const string IndexFile = "MEMORY.md";

    /// <summary>The store directory (empty = disabled/no-op).</summary>
    public string DirectoryPath { get; }

    /// <summary>Resolve the memory store under the workspace.</summary>
    public MemoryStore(string workspaceRoot)
    {
        DirectoryPath = Path.Combine(
            Path.GetFullPath(workspaceRoot),
            ".reasonet", "memory");
    }

    /// <summary>Disabled store (no-op).</summary>
    public MemoryStore() { DirectoryPath = ""; }

    public bool IsEnabled => !string.IsNullOrEmpty(DirectoryPath);

    /// <summary>Index: MEMORY.md contents, or "" if none yet.</summary>
    public string Index()
    {
        if (!IsEnabled) return "";
        var path = Path.Combine(DirectoryPath, IndexFile);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
    }

    /// <summary>Absolute path for a named memory fact file.</summary>
    public string FactPath(string name) =>
        IsEnabled ? Path.Combine(DirectoryPath, MemoryHelpers.Slugify(name) + ".md") : "";

    /// <summary>Save a memory fact; creates/overwrites the file and refreshes the index.</summary>
    public string Save(MemoryFact fact)
    {
        if (!IsEnabled)
            throw new InvalidOperationException("memory store unavailable");
        var name = MemoryHelpers.Slugify(fact.Name);
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("memory needs a name");

        Directory.CreateDirectory(DirectoryPath);

        var path = Path.Combine(DirectoryPath, name + ".md");
        File.WriteAllText(path, Render(fact, name));
        Reindex(name, fact);
        return path;
    }

    /// <summary>Delete a memory by name; removes file and index line.</summary>
    public void Delete(string name)
    {
        if (!IsEnabled) return;
        name = MemoryHelpers.Slugify(name);
        if (string.IsNullOrEmpty(name)) return;

        var path = Path.Combine(DirectoryPath, name + ".md");
        if (File.Exists(path)) File.Delete(path);

        FlushIndex(IndexLinesExcept(name));
    }

    #region Internal

    /// <summary>Match index lines to extract the filename stem.</summary>
    private static readonly Regex IndexLineRegex = new(@"\]\(([^)]+)\.md\)");

    private string Render(MemoryFact fact, string name)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"name: {name}");
        if (!string.IsNullOrWhiteSpace(fact.Title))
            sb.AppendLine($"title: {MemoryHelpers.OneLine(fact.Title)}");
        sb.AppendLine($"description: {MemoryHelpers.OneLine(fact.Description)}");
        sb.AppendLine("metadata:");
        sb.AppendLine($"  type: {MemoryHelpers.TypeName(fact.Type)}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(fact.Body.Trim());
        return sb.ToString();
    }

    private Dictionary<string, string> IndexLinesExcept(string name)
    {
        var indexFile = Path.Combine(DirectoryPath, IndexFile);
        var keep = new Dictionary<string, string>();
        if (!File.Exists(indexFile)) return keep;

        foreach (var line in File.ReadAllLines(indexFile))
        {
            var mt = IndexLineRegex.Match(line);
            if (mt.Success && mt.Groups[1].Value != name)
                keep[mt.Groups[1].Value] = line.TrimEnd('\r');
        }
        return keep;
    }

    private void FlushIndex(Dictionary<string, string> lines)
    {
        var names = lines.Keys.OrderBy(n => n).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("# Memory");
        sb.AppendLine();
        foreach (var n in names)
        {
            sb.AppendLine(lines[n]);
        }
        File.WriteAllText(Path.Combine(DirectoryPath, IndexFile), sb.ToString());
    }

    private void Reindex(string name, MemoryFact fact)
    {
        var lines = IndexLinesExcept(name);
        var title = MemoryHelpers.DisplayTitle(fact.Title, name);
        var desc = MemoryHelpers.OneLine(fact.Description);
        lines[name] = $"- [{title}]({name}.md) — {desc}";
        FlushIndex(lines);
    }

    #endregion
}
