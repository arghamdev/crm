using System.Reflection;

namespace Crm.Web.Presentation;

public sealed record ReleaseNote(string Version, string Date, string Title, IReadOnlyList<string> Changes);

/// <summary>
/// The product version (from &lt;Version&gt; in Directory.Build.props) and the release notes embedded from CHANGELOG.md.
/// Entries are "## 1.8.0 — 1405/07/12 — title" followed by "- change" lines; the first entry is the current release.
/// </summary>
public static class SystemVersion
{
    public static string Current { get; } = typeof(SystemVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        .Split('+')[0] ?? "0.0.0";

    public static IReadOnlyList<ReleaseNote> Releases { get; } = Parse();

    /// <summary>Release date of the running version (Jalali), or null when the changelog has no entry for it.</summary>
    public static string? ReleasedOn => Releases.FirstOrDefault(x => x.Version == Current)?.Date;

    private static List<ReleaseNote> Parse()
    {
        using var stream = typeof(SystemVersion).Assembly.GetManifestResourceStream("CHANGELOG.md");
        if (stream is null) return [];
        using var reader = new StreamReader(stream);
        var releases = new List<ReleaseNote>();
        string? version = null, date = null, title = null;
        var changes = new List<string>();
        void Flush()
        {
            if (version is not null) releases.Add(new ReleaseNote(version, date ?? "", title ?? "", changes.ToList()));
            changes.Clear();
        }
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                var parts = line[3..].Split('—', StringSplitOptions.TrimEntries);
                version = parts[0];
                date = parts.Length > 1 ? parts[1] : null;
                title = parts.Length > 2 ? string.Join(" — ", parts[2..]) : null;
            }
            else if (version is not null && line.StartsWith("- ", StringComparison.Ordinal)) changes.Add(line[2..].Trim());
        }
        Flush();
        return releases;
    }
}
