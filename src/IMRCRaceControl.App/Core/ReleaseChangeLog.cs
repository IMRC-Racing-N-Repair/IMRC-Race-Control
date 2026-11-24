namespace IMRCRaceControl.Core;

public static class ReleaseChangeLog
{
    public sealed record Section(Version Version, IReadOnlyList<string> Changes);

    public static IReadOnlyList<Section> Select(string markdown, Version current, Version? previous)
    {
        if (previous is not null && previous >= current) return [];
        var sections = new List<Section>();
        Version? version = null;
        List<string>? changes = null;
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                version = Version.TryParse(line[3..].Trim(), out var parsed) ? parsed : null;
                changes = new List<string>();
                if (version is not null) sections.Add(new(version, changes));
            }
            else if (version is not null && changes is not null && line.StartsWith("- ", StringComparison.Ordinal))
                changes.Add(line[2..].Replace("**", "").Replace("`", ""));
        }
        var eligible = sections.Where(s => s.Version <= current &&
            (previous is null || s.Version > previous)).OrderByDescending(s => s.Version);
        // The first version with this feature cannot know the exact old version.
        var selected = previous is null ? eligible.Take(5) : eligible;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return selected.Select(s => new Section(s.Version, s.Changes.Where(seen.Add).ToList()))
            .Where(s => s.Changes.Count > 0).ToList();
    }
}
