using System.IO;
using System.Text.RegularExpressions;

namespace Ultrahand;

/// What changed, from the CHANGELOG.md built into the .exe: its "## X.Y.Z" sections as plain text, for the popup a launcher update
/// shows on its first start (MainWindow.Notes.cs)
static class ReleaseNotes
{
    public sealed record Section(string Version, List<string> Items);

    /// The sections newer than after, up to and with upTo, newest first; after null: upTo's section alone
    public static List<Section> Between(string? after, string upTo)
    {
        var sections = new List<Section>();
        using var stream = typeof(ReleaseNotes).Assembly.GetManifestResourceStream("CHANGELOG.md");
        if (stream == null) return sections;
        using var reader = new StreamReader(stream);
        Section? current = null;
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("## "))
            {
                var version = line[3..].Trim();
                var wanted = after == null ? Versions.Compare(version, upTo) == 0 : Versions.Newer(version, after) && !Versions.Newer(version, upTo);
                current = wanted ? new Section(version, new List<string>()) : null;
                if (current != null) sections.Add(current);
                continue;
            }
            if (current == null || line.Trim().Length == 0) continue;
            var text = Plain(line.Trim());
            if (line.StartsWith("- ")) current.Items.Add(text[2..]);
            else if (line.StartsWith("  ") && current.Items.Count > 0) current.Items[^1] += " " + text;   // (a bullet's next line)
            else current.Items.Add(text);
        }
        return sections;
    }

    /// Markdown to plain text: no bold or code marks, links as their text
    static string Plain(string line) =>
        Regex.Replace(line.Replace("**", "").Replace("`", ""), @"\[([^\]]+)\]\([^)]+\)", "$1");
}
