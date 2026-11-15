using System.IO;
using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace IMRCRaceControl.Services;

public enum RaceImportFormat { RaceControl, LiveTime, RcScoringPro, Unknown }

public sealed record RaceImportInspection(RaceImportFormat Format, string Description,
    IReadOnlyList<string> Tables, IReadOnlyList<string> Notes)
{
    public bool IsImportSupported => Format is RaceImportFormat.LiveTime or RaceImportFormat.RcScoringPro;
}

/// <summary>
/// Inspects sources read-only. File extensions are not trusted to identify a database.
/// Unknown or partially supported formats are never passed to a writer.
/// </summary>
public static class RaceImportDetector
{
    private static readonly string[] RcspTables =
    [
        "Driver.adt", "Classes.adt", "DriverClasses.adt", "Entries.adt",
        "Practice.adt", "PracticeRuns.adt", "Tracks.adt"
    ];

    public static RaceImportInspection Inspect(string path)
    {
        if (Directory.Exists(path))
        {
            var files = Directory.GetFiles(path, "*.adt", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName).Where(s => s is not null).Select(s => s!).ToArray();
            return InspectRcspNames(files, "RC Scoring Pro Advantage table directory");
        }
        if (!File.Exists(path)) throw new FileNotFoundException("Source was not found.", path);
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> header = stackalloc byte[16];
        var count = stream.Read(header);
        stream.Position = 0;
        if (count >= 16 && header.SequenceEqual("SQLite format 3\0"u8))
            return InspectSqlite(path);
        if (count >= 4 && header[0] == (byte)'P' && header[1] == (byte)'K')
            return InspectZip(stream);
        if (count >= 15 && header[..15].SequenceEqual("Advantage Table"u8))
            return InspectRcspNames(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.adt")
                .Select(Path.GetFileName).Where(s => s is not null).Select(s => s!).ToArray(),
                "RC Scoring Pro Advantage table");
        // Encrypted LiveTime databases do not have a SQLite plaintext signature.
        // Never automatically assume that arbitrary binary data is LiveTime.
        return Unknown("Unrecognized source format; no data has been changed.");
    }

    private static RaceImportInspection InspectSqlite(string path)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var reader = cmd.ExecuteReader())
            while (reader.Read()) names.Add(reader.GetString(0));
        var ordered = names.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Contains("AppState") && names.Contains("EventSessions") && names.Contains("Drivers"))
            return new(RaceImportFormat.RaceControl, "Native IMRC Race Control database", ordered,
                ["Use RESTORE DATABASE for native backups. Restore is separate from selective LiveTime imports."]);
        if (names.Contains("RaceClass") && names.Contains("Driver"))
            return new(RaceImportFormat.LiveTime, "LiveTime SQLite database", ordered, []);
        return new(RaceImportFormat.Unknown, "SQLite database with an unrecognized schema", ordered,
            ["Not imported: schema identity could not be verified."]);
    }

    private static RaceImportInspection InspectZip(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        // Only inspect archive metadata: do not extract paths or execute bundled files.
        if (zip.Entries.Count > 20000)
            return Unknown("Archive has too many entries for a safe preview.");
        var core = zip.Entries.Select(e => e.FullName.Replace('\\', '/'))
            .Where(n => !n.EndsWith('/'))
            .Where(n => n.Contains("/Data/", StringComparison.OrdinalIgnoreCase))
            .Where(n => !n.Contains("/Blank/", StringComparison.OrdinalIgnoreCase)
                && !n.Contains("/Backup/", StringComparison.OrdinalIgnoreCase)
                && !n.Contains("/debug/", StringComparison.OrdinalIgnoreCase))
            .Where(n => n.EndsWith(".adt", StringComparison.OrdinalIgnoreCase))
            .Select(n => n[(n.LastIndexOf('/') + 1)..]).ToArray();
        var identified = InspectRcspNames(core, "RC Scoring Pro backup ZIP");
        if (identified.Format != RaceImportFormat.RcScoringPro) return identified;
        var notes = identified.Notes.ToList();
        foreach (var name in new[] { "Driver", "Classes", "DriverClasses", "Entries",
                                     "Practice", "PracticeRuns", "Tracks", "LoanerTransponders" })
        {
            if (!core.Contains(name + ".adt", StringComparer.OrdinalIgnoreCase)) continue;
            var table = RcspAdvantageTable.FromZip(zip, name);
            var active = table.ActiveRows().Count();
            notes.Add($"{name}: {active:N0} live records / {table.PhysicalRecordCount:N0} physical, {table.Fields.Count} fields.");
        }
        return identified with { Notes = notes };
    }

    private static RaceImportInspection InspectRcspNames(IEnumerable<string> fileNames, string description)
    {
        var names = new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase);
        var identified = RcspTables.Where(names.Contains).ToArray();
        if (!names.Contains("Driver.adt") || !names.Contains("Classes.adt") ||
            (!names.Contains("Entries.adt") && !names.Contains("Practice.adt")))
            return Unknown("This source does not contain a recognizable RC Scoring Pro table set.");
        var missing = RcspTables.Where(s => !names.Contains(s)).ToArray();
        var notes = new List<string>
        {
            "Source recognized. RC Scoring Pro uses proprietary Advantage ADT/ADI/ADM files, not SQLite.",
            "Read-only preview only: decoding of the full record and lap schema has not yet been validated.",
            "No source or Race Control records will be modified by this inspection."
        };
        if (missing.Length > 0) notes.Add("Missing optional/expected tables: " + string.Join(", ", missing));
        return new(RaceImportFormat.RcScoringPro, description, identified, notes);
    }

    private static RaceImportInspection Unknown(string reason) =>
        new(RaceImportFormat.Unknown, "Unrecognized race data", [], [reason]);
}
