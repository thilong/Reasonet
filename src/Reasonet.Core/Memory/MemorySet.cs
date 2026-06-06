using System.Text;

namespace Reasonet.Memory;

/// <summary>
/// Everything memory loaded for one session: hierarchical docs plus the
/// auto-memory store handle. Assembled once at boot and folded into the
/// system prompt by <see cref="Compose"/>.
/// </summary>
public sealed class MemorySet
{
    private const string QuickAddHeading = "## Notes";

    /// <summary>Doc memory files, ascending precedence order.</summary>
    public IReadOnlyList<DocSource> Docs { get; }
    /// <summary>Auto-memory store handle.</summary>
    public MemoryStore Store { get; }
    /// <summary>MEMORY.md index contents at load time.</summary>
    public string Index { get; }
    /// <summary>Workspace directory for discovery.</summary>
    public string WorkspaceRoot { get; }

    /// <summary>Recognized memory filenames (cross-tool compatible).</summary>
    private static readonly string[] DocNames = { "REASONET.md", "AGENTS.md", "CLAUDE.md", "REASONIX.md" };
    private static readonly string[] LocalNames = { "REASONET.local.md", "AGENTS.local.md", "CLAUDE.local.md", "REASONIX.local.md" };
    private const string DefaultDocName = "AGENTS.md";
    private const string DefaultLocalName = "AGENTS.local.md";

    private MemorySet(IReadOnlyList<DocSource> docs, MemoryStore store, string index, string workspaceRoot)
    {
        Docs = docs;
        Store = store;
        Index = index;
        WorkspaceRoot = workspaceRoot;
    }

    /// <summary>
    /// Discover all memory for a session. Best-effort — missing files just
    /// mean less memory.
    /// </summary>
    public static MemorySet Load(string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var store = new MemoryStore(root);
        var docs = DiscoverDocs(root);
        return new MemorySet(docs, store, store.Index(), root);
    }

    /// <summary>
    /// Discover doc-memory files: project docs, then local overrides.
    /// All found under &lt;workspace&gt;/.reasonet/.
    /// </summary>
    internal static List<DocSource> DiscoverDocs(string workspaceRoot)
    {
        var dotDir = Path.Combine(workspaceRoot, ".reasonet");
        var result = new List<DocSource>();

        // Project scope: <workspace>/.reasonet/AGENTS.md etc.
        foreach (var name in DocNames)
        {
            var path = Path.Combine(dotDir, name);
            var (body, ok) = ReadDoc(path);
            if (ok)
                result.Add(new DocSource { Path = path, Scope = DocScope.Project, Body = body });
        }

        // Local scope (highest precedence): <workspace>/.reasonet/AGENTS.local.md etc.
        foreach (var name in LocalNames)
        {
            var path = Path.Combine(dotDir, name);
            var (body, ok) = ReadDoc(path);
            if (ok)
                result.Add(new DocSource { Path = path, Scope = DocScope.Local, Body = body });
        }

        return result;
    }

    /// <summary>Read and @import-expand a doc file.</summary>
    internal static (string Body, bool Ok) ReadDoc(string path)
    {
        try
        {
            if (!File.Exists(path)) return ("", false);
            var text = File.ReadAllText(path);
            var baseDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
            var seen = new HashSet<string> { MemoryHelpers.AbsOf(path) };
            var body = MemoryHelpers.ResolveImports(text, baseDir, seen, 0);
            return (body, true);
        }
        catch
        {
            return ("", false);
        }
    }

    /// <summary>Return the canonical doc path for a given scope.</summary>
    public string DocPath(DocScope scope)
    {
        var dotDir = Path.Combine(WorkspaceRoot, ".reasonet");
        return scope switch
        {
            DocScope.Local => FindExisting(dotDir, LocalNames) ?? Path.Combine(dotDir, DefaultLocalName),
            _ => FindExisting(dotDir, DocNames) ?? Path.Combine(dotDir, DefaultDocName),
        };
    }

    private static string? FindExisting(string dir, string[] names)
    {
        foreach (var n in names)
        {
            var p = Path.Combine(dir, n);
            if (File.Exists(p)) return Path.GetFullPath(p);
        }
        return null;
    }

    /// <summary>Report whether this set carries nothing to inject.</summary>
    public bool IsEmpty => Docs.Count == 0 && string.IsNullOrWhiteSpace(Index);

    /// <summary>Render the memory as a single Markdown block for prompt injection.</summary>
    public string Block()
    {
        if (IsEmpty) return "";
        var sb = new StringBuilder();
        sb.AppendLine("# Memory");
        sb.AppendLine();
        sb.AppendLine("Persistent context loaded from memory files. Treat it as durable, user-authored guidance for this project.");
        sb.AppendLine();

        foreach (var d in Docs)
        {
            var scopeLabel = d.Scope == DocScope.Local ? "local" : "project";
            sb.AppendLine($"## {d.Path} ({scopeLabel})");
            sb.AppendLine();
            sb.AppendLine(d.Body.Trim());
            sb.AppendLine();
        }

        var idx = Index?.Trim();
        if (!string.IsNullOrEmpty(idx))
        {
            sb.AppendLine("## Saved memories");
            sb.AppendLine();
            sb.AppendLine("Facts you saved in earlier sessions. They reflect what was true when written and may now be stale. " +
                "Read the linked file with read_file when one looks relevant. " +
                "Save new durable facts with the `remember` tool; delete ones that turn out wrong with `forget`.");
            sb.AppendLine();
            sb.AppendLine(idx);
            sb.AppendLine();
            if (Store.IsEnabled)
                sb.AppendLine($"(stored under {Store.DirectoryPath})");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Fold the memory block onto the base system prompt. Returns the combined
    /// string (cache-stable prefix). With no memory, base is returned unchanged.
    /// </summary>
    public static string Compose(string basePrompt, MemorySet? set)
    {
        if (set == null || set.IsEmpty) return basePrompt;
        var block = set.Block();
        if (string.IsNullOrWhiteSpace(block)) return basePrompt;
        return basePrompt.TrimEnd('\n') + "\n\n" + block;
    }

    /// <summary>
    /// Write (overwrite) a doc-memory file after checking it's a recognized path.
    /// The write lands on disk immediately but does NOT mutate the cache prefix.
    /// </summary>
    public string WriteDoc(string path, string body)
    {
        if (!AllowedDocPaths().Contains(MemoryHelpers.AbsOf(path)))
            throw new InvalidOperationException($"refusing to write \"{path}\": not a recognized memory file");

        WriteDocFile(path, body);
        return path;
    }

    /// <summary>
    /// Append a note as a bullet under "## Notes" in the project doc file.
    /// Creates the file and section when absent.
    /// </summary>
    public string AppendNote(string note)
    {
        note = MemoryHelpers.OneLine(note);
        if (string.IsNullOrEmpty(note)) return "";

        var path = DocPath(DocScope.Project);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string body;
        if (File.Exists(path))
            body = File.ReadAllText(path);
        else
            body = "# Project memory\n\n" + QuickAddHeading + "\n\n";

        var bullet = "- " + note;
        if (body.Contains(QuickAddHeading))
            body = InsertUnderHeading(body, QuickAddHeading, bullet);
        else
            body = body.TrimEnd('\n') + "\n\n" + QuickAddHeading + "\n\n" + bullet + "\n";

        WriteDocFile(path, body);
        return path;
    }

    private HashSet<string> AllowedDocPaths()
    {
        var allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DocScope sc in Enum.GetValues<DocScope>())
        {
            var p = DocPath(sc);
            if (!string.IsNullOrEmpty(p)) allow.Add(MemoryHelpers.AbsOf(p));
        }
        foreach (var d in Docs)
            allow.Add(MemoryHelpers.AbsOf(d.Path));
        return allow;
    }

    private static void WriteDocFile(string path, string body)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, body.TrimEnd('\n') + "\n");
    }

    private static string InsertUnderHeading(string body, string heading, string bullet)
    {
        var lines = body.Split('\n');
        var start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() == heading) { start = i; break; }
        }
        if (start < 0) return body.TrimEnd('\n') + "\n\n" + bullet + "\n";

        var end = lines.Length;
        for (int i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].Trim().StartsWith('#')) { end = i; break; }
        }

        var sb = new StringBuilder();
        for (int i = 0; i < end; i++) sb.AppendLine(lines[i]);
        sb.AppendLine(bullet);
        for (int i = end; i < lines.Length; i++) sb.AppendLine(lines[i]);
        return sb.ToString();
    }
}
