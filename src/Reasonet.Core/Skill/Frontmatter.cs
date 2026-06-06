namespace Reasonet.Skill;

/// <summary>
/// Minimal dependency-free frontmatter parser for ---fenced "key: value" blocks.
/// Mirrors the YAML-like convention without a YAML library.
/// </summary>
public static class Frontmatter
{
    /// <summary>
    /// Split separates an optional leading ---fenced block of "key: value" lines
    /// from the body. Returns parsed keys (lowercased) and the remaining body.
    /// With no opening/closing fence the whole input is the body.
    /// </summary>
    public static (Dictionary<string, string> Metadata, string Body) Split(string text)
    {
        var fm = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        text = text.Replace("\r\n", "\n");
        var lines = text.Split('\n');

        if (lines.Length == 0 || lines[0].Trim() != "---")
            return (fm, text);

        int closingIdx = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                closingIdx = i;
                break;
            }
        }

        if (closingIdx < 0)
            return (fm, text); // opened but never closed: treat all as body

        var content = lines[1..closingIdx];
        for (int j = 0; j < content.Length; j++)
        {
            var colonIdx = content[j].IndexOf(':');
            if (colonIdx < 0) continue;

            var key = content[j][..colonIdx].Trim().ToLowerInvariant();
            var val = content[j][(colonIdx + 1)..].Trim().Trim('"').Trim('\'');

            if (val.Length == 0)
            {
                // Empty value: section header or YAML list
                var items = new List<string>();
                while (j + 1 < content.Length)
                {
                    var next = content[j + 1].Trim();
                    if (!next.StartsWith("- ")) break;
                    items.Add(next[2..].Trim().Trim('"').Trim('\''));
                    j++;
                }
                if (items.Count > 0)
                {
                    fm[key] = string.Join(", ", items);
                }
                continue;
            }

            fm[key] = val;
        }

        var body = string.Join("\n", lines[(closingIdx + 1)..]);
        return (fm, body);
    }
}
