using System.Text.RegularExpressions;

namespace Reasonet.Memory;

/// <summary>
/// Internal helpers for the memory system.
/// </summary>
public static class MemoryHelpers
{
    /// <summary>Normalize a name to a filesystem-safe slug.</summary>
    public static string Slugify(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        // Lowercase, replace non-alphanumeric with '-', collapse multiple '-'
        var slug = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-");
        return slug.Trim('-');
    }

    /// <summary>Collapse to a single line.</summary>
    public static string OneLine(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return Regex.Replace(s.Replace('\n', ' ').Replace('\r', ' '), @"\s+", " ").Trim();
    }

    /// <summary>Absolute path, resolving symlinks.</summary>
    public static string AbsOf(string path) =>
        Path.GetFullPath(path);

    /// <summary>Check if two normalized paths point to the same directory.</summary>
    public static bool SameDir(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar).TrimEnd(Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar).TrimEnd(Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Derive a display title from a title override or de-kebab the slug.</summary>
    public static string DisplayTitle(string? title, string name)
    {
        if (!string.IsNullOrWhiteSpace(title))
            return OneLine(title);
        // De-kebab: "prefers-tabs" → "Prefers tabs"
        return string.Join(' ', name.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(Word => char.ToUpper(Word[0]) + Word[1..]));
    }

    /// <summary>Normalize a memory type string to the enum.</summary>
    public static MemoryType NormalizeType(string type) =>
        type.ToLowerInvariant() switch
        {
            "user" => MemoryType.User,
            "feedback" => MemoryType.Feedback,
            "reference" => MemoryType.Reference,
            _ => MemoryType.Project,
        };

    /// <summary>Serialize a MemoryType to its config name.</summary>
    public static string TypeName(MemoryType t) => t switch
    {
        MemoryType.User => "user",
        MemoryType.Feedback => "feedback",
        MemoryType.Reference => "reference",
        _ => "project",
    };

    /// <summary>
    /// Resolve @path imports in document bodies — inline the content of referenced files.
    /// Recursion up to maxDepth prevents cycles.
    /// </summary>
    public static string ResolveImports(string body, string baseDir, HashSet<string> seen, int depth, int maxDepth = 5)
    {
        if (depth > maxDepth) return body;

        var result = body;
        var importPattern = @"^@(.+)$";
        foreach (Match m in Regex.Matches(body, importPattern, RegexOptions.Multiline))
        {
            var importPath = m.Groups[1].Value.Trim();
            var fullPath = Path.IsPathRooted(importPath)
                ? Path.GetFullPath(importPath)
                : Path.GetFullPath(Path.Combine(baseDir, importPath));

            if (!File.Exists(fullPath) || !seen.Add(fullPath))
            {
                result = result.Replace(m.Value, $"*@import failed: {importPath}*");
                continue;
            }

            try
            {
                var imported = File.ReadAllText(fullPath);
                imported = ResolveImports(imported, Path.GetDirectoryName(fullPath) ?? baseDir, seen, depth + 1, maxDepth);
                result = result.Replace(m.Value, imported.TrimEnd());
            }
            catch
            {
                result = result.Replace(m.Value, $"*@import error: {importPath}*");
            }
        }
        return result;
    }
}
