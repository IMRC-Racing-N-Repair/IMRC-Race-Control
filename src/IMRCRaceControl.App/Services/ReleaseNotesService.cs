using System.IO;
using System.Reflection;
using IMRCRaceControl.Core;

namespace IMRCRaceControl.Services;

internal static class ReleaseNotesService
{
    internal static Version CurrentVersion
    {
        get
        {
            var version = typeof(ReleaseNotesService).Assembly.GetName().Version ?? new Version(0,0,0);
            return new Version(version.Major,version.Minor,Math.Max(0,version.Build));
        }
    }
    private static string MarkerPath => Path.Combine(ApplicationDataPaths.DataDirectory,"last-opened-version.txt");
    internal static Version? ReadPreviousVersion() =>
        File.Exists(MarkerPath) && Version.TryParse(File.ReadAllText(MarkerPath).Trim(),out var version) ? version : null;

    internal static IReadOnlyList<ReleaseChangeLog.Section> GetChanges(Version current, Version? previous)
    {
        using var stream = typeof(ReleaseNotesService).Assembly.GetManifestResourceStream("IMRCRaceControl.Changelog")
            ?? throw new InvalidDataException("Bundled changelog is missing.");
        using var reader = new StreamReader(stream);
        return ReleaseChangeLog.Select(reader.ReadToEnd(),current,previous);
    }
    internal static void RememberVersion(Version version)
    {
        Directory.CreateDirectory(ApplicationDataPaths.DataDirectory);
        var temp = MarkerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp,version.ToString(3));
            File.Move(temp,MarkerPath,true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
