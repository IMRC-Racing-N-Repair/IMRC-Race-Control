using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace IMRCRaceControl.Services;

public sealed record LiveTimeDriver(long LocalId, Guid SourceId, string FirstName, string LastName, string Nickname, string PhoneticName);
public sealed record LiveTimeClass(long LocalId, Guid SourceId, string Name, string Color, double? MinimumLapSeconds);
public sealed record LiveTimeClassEntry(long DriverId, long ClassId, string Transponders);
public sealed record LiveTimeRoster(int SchemaVersion, List<LiveTimeDriver> Drivers, List<LiveTimeClass> Classes,
    List<LiveTimeClassEntry> Entries, long Events, long Races, long Laps, long PracticeLaps, long Rentals)
{
    public List<string> RentalNumbers { get; init; } = [];
}

public static partial class LiveTimeBackupReader
{
    private const int LegacyPageSize = 1024;
    private const int MaxEncodedBytes = 256 * 1024 * 1024;
    private static ReadOnlySpan<byte> Header => "SQLite format 3\0"u8;

    public static LiveTimeRoster Read(string path, string? referencePath = null) =>
        ReadFile(path, referencePath, ReadRoster, promptForPassword: false);

    public static LiveTimeImportSource ReadImport(string path, string? referencePath = null) =>
        ReadFile(path, referencePath, ReadImportSource);

    private static LiveTimeImportSource ReadImportSource(SqliteConnection db)
    {
        var roster = ReadRoster(db);
        return new LiveTimeImportSource(roster, roster.SchemaVersion == 325 ? ReadRaceHistory(db) : []);
    }

    private static T ReadFile<T>(string path, string? referencePath, Func<SqliteConnection,T> read, bool promptForPassword = true) where T : class
    {
        if (IsPlainDatabase(path))
        {
            // Disable pooling so the backup file is released as soon as the preview ends.
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            db.Open();
            return read(db);
        }

        var encoded = ReadEncoded(path);
        try
        {
            if (TryReadEncoded(encoded, encoded, read, out var roster))
                return roster!;

            if (!string.IsNullOrWhiteSpace(referencePath))
            {
                var reference = ReadEncoded(referencePath);
                try
                {
                    if (TryReadEncoded(encoded, reference, read, out roster))
                        return roster!;
                }
                finally { CryptographicOperations.ZeroMemory(reference); }
            }

            if (promptForPassword && CouldBeSqlCipher(encoded.Length))
                throw new LiveTimeBackupPasswordRequiredException();

            throw new InvalidDataException("This backup could not be opened. For an older backup, try a matching reference.");
        }
        finally { CryptographicOperations.ZeroMemory(encoded); }
    }

    private static bool IsPlainDatabase(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[16];
        return stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length
            && header.SequenceEqual(Header);
    }

    private static byte[] ReadEncoded(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length < LegacyPageSize || stream.Length > MaxEncodedBytes || stream.Length % LegacyPageSize != 0)
            throw new InvalidDataException("This file does not match the supported legacy LiveTime backup layout (maximum 256 MB).");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    // The verified legacy layout applies the same byte mask to each 1 KiB page.
    // Repeated unused pages supply candidates. No vendor/user password or recovered
    // mask is stored. A candidate is accepted only after full SQLite integrity and
    // LiveTime schema checks; this does not assume newer backups use the same layout.
    private static bool TryReadEncoded<T>(byte[] encoded, byte[] reference, Func<SqliteConnection,T> read, out T? roster) where T : class
    {
        roster = null;
        var signatures = new Dictionary<ulong, (int Offset, int Count)>();
        Span<byte> digest = stackalloc byte[32];
        for (var i = 0; i < reference.Length; i += LegacyPageSize)
        {
            SHA256.HashData(reference.AsSpan(i, LegacyPageSize), digest);
            var signature = BitConverter.ToUInt64(digest);
            var previous = signatures.GetValueOrDefault(signature, (Offset:i, Count:0));
            signatures[signature] = (previous.Offset, previous.Count + 1);
        }

        foreach (var candidate in signatures.Values.Where(v => v.Count >= 2).OrderByDescending(v => v.Count).Take(8))
        {
            var mask = reference.AsSpan(candidate.Offset, LegacyPageSize).ToArray();
            byte[]? decoded = null;
            try
            {
                for (var i = 0; i < Header.Length; i++) mask[i] = (byte)(reference[i] ^ Header[i]);
                if (!HeaderMatches(encoded, mask) || (encoded[16] ^ mask[16]) != 4 || (encoded[17] ^ mask[17]) != 0)
                    continue;

                decoded = new byte[encoded.Length];
                for (var i = 0; i < encoded.Length; i++) decoded[i] = (byte)(encoded[i] ^ mask[i % LegacyPageSize]);
                using var db = OpenInMemory(decoded);
                roster = read(db);
                return true;
            }
            catch (SqliteException) { }
            catch (InvalidDataException) { }
            finally
            {
                CryptographicOperations.ZeroMemory(mask);
                if (decoded is not null) CryptographicOperations.ZeroMemory(decoded);
            }
        }
        return false;
    }

    private static bool HeaderMatches(byte[] encoded, byte[] mask)
    {
        for (var i = 0; i < Header.Length; i++)
            if ((encoded[i] ^ mask[i]) != Header[i]) return false;
        return true;
    }

    // Use the same SQLite library that Race Control already ships. Deserialize into
    // a read-only in-memory connection; the decoded backup never becomes a disk file.
    private static SqliteConnection OpenInMemory(byte[] bytes)
    {
        var db = new SqliteConnection("Data Source=:memory:;Pooling=False");
        try
        {
            db.Open();
            if (bytes[18] != 1 || bytes[19] != 1)
                throw new InvalidDataException("Use a standalone backup rather than a database that still needs its journal.");
            var memory = SqliteMalloc((ulong)bytes.Length);
            if (memory == IntPtr.Zero) throw new OutOfMemoryException("The backup could not fit in memory.");
            try { Marshal.Copy(bytes, 0, memory, bytes.Length); }
            catch { SqliteFree(memory); throw; }
            // FREEONCLOSE (1) also frees memory on failure; READONLY (4).
            if (SqliteDeserialize(db.Handle!.DangerousGetHandle(), "main", memory, bytes.Length, bytes.Length, 5) != 0)
                throw new InvalidDataException("The decoded backup could not be opened.");
            return db;
        }
        catch { db.Dispose(); throw; }
    }

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_malloc64", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SqliteMalloc(ulong count);
    [DllImport("e_sqlite3", EntryPoint = "sqlite3_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SqliteFree(IntPtr memory);
    [DllImport("e_sqlite3", EntryPoint = "sqlite3_deserialize", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SqliteDeserialize(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string schema,
        IntPtr memory, long size, long capacity, uint flags);

    private static LiveTimeRoster ReadRoster(SqliteConnection db)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA trusted_schema=OFF; PRAGMA query_only=ON;";
        cmd.ExecuteNonQuery();

        var tables = Read(db, "SELECT name FROM sqlite_schema WHERE type='table'", r => r.GetString(0))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (new[] { "SystemVersion", "Driver", "RaceClass", "DriverRaceClassTransponder", "Event", "Race", "RaceLap" }
            .Any(table => !tables.Contains(table)))
            throw new InvalidDataException("The file does not contain the expected LiveTime race tables.");

        cmd.CommandText = "PRAGMA integrity_check(1);";
        if (!string.Equals(cmd.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
            throw new InvalidDataException("The LiveTime backup did not pass its integrity check.");

        cmd.CommandText = "SELECT [Database] FROM SystemVersion LIMIT 1";
        var version = Convert.ToInt32(cmd.ExecuteScalar());
        if (version <= 0) throw new InvalidDataException("The LiveTime backup has no database version.");
        var drivers = Read(db, "SELECT LID,GUID,FirstName,LastName,NickName,PhoneticName FROM Driver WHERE IsActive=1 ORDER BY LID",
            r => new LiveTimeDriver(r.GetInt64(0), GuidValue(r,1), TextValue(r,2), TextValue(r,3), TextValue(r,4), TextValue(r,5)));
        var classes = Read(db, "SELECT LID,GUID,Name,Color FROM RaceClass WHERE IsActive=1 ORDER BY LID",
            r => new LiveTimeClass(r.GetInt64(0), GuidValue(r,1), TextValue(r,2), TextValue(r,3), null));
        if (tables.Contains("RaceClassDefaultSetting"))
        {
            var minimums = Read(db, "SELECT RaceClassLID,MinimumLapTimeSeconds FROM RaceClassDefaultSetting ORDER BY LID",
                r => (Id:r.GetInt64(0), Seconds:r.IsDBNull(1) ? (double?)null : r.GetDouble(1)))
                .GroupBy(x=>x.Id).ToDictionary(g=>g.Key,g=>g.Last().Seconds);
            classes = classes.Select(c=>c with {MinimumLapSeconds=minimums.GetValueOrDefault(c.LocalId)}).ToList();
        }

        var tags = Read(db, "SELECT DriverLID,RaceClassLID,TransponderNumber FROM DriverRaceClassTransponder ORDER BY IsPrimary DESC,LID",
            r => (Driver:r.GetInt64(0), Class:r.GetInt64(1), Tag:r.IsDBNull(2) ? 0 : r.GetInt64(2)));
        var relationships = tags.Select(x => (x.Driver,x.Class)).ToHashSet();
        if (tables.Contains("DriverRaceClassCarInformation"))
            relationships.UnionWith(Read(db, "SELECT DISTINCT DriverLID,RaceClassLID FROM DriverRaceClassCarInformation",
                r => (r.GetInt64(0),r.GetInt64(1))));
        var tagLookup = tags.Where(t=>t.Tag>0).ToLookup(t=>(t.Driver,t.Class), t=>t.Tag);
        var activeDrivers=drivers.Select(d=>d.LocalId).ToHashSet();
        var activeClasses=classes.Select(c=>c.LocalId).ToHashSet();
        var entries = relationships.Where(x=>activeDrivers.Contains(x.Driver)&&activeClasses.Contains(x.Class))
            .OrderBy(x=>x.Driver).ThenBy(x=>x.Class)
            .Select(x=>new LiveTimeClassEntry(x.Driver,x.Class,string.Join(";",tagLookup[x].Distinct()))).ToList();

        long Count(string table)
        {
            if (!tables.Contains(table)) return 0;
            cmd.CommandText = $"SELECT count(*) FROM [{table}]";
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        return new(version,drivers,classes,entries,Count("Event"),Count("Race"),Count("RaceLap"),
            Count("PracticeLap"),Count("TransponderRental"))
        {
            RentalNumbers = tables.Contains("TransponderRental")
                ? Read(db,"SELECT CAST(TransponderNumber AS TEXT) FROM TransponderRental WHERE TransponderNumber > 0",r=>r.GetString(0))
                : []
        };
    }

    private static List<T> Read<T>(SqliteConnection db, string query, Func<SqliteDataReader,T> row)
    {
        using var command=db.CreateCommand();
        command.CommandText=query;
        using var reader=command.ExecuteReader();
        var rows=new List<T>();
        while(reader.Read())rows.Add(row(reader));
        return rows;
    }

    private static string TextValue(SqliteDataReader r,int i) => r.IsDBNull(i) ? "" : r.GetString(i).Trim();
    private static Guid GuidValue(SqliteDataReader r,int i) => r.GetValue(i) switch
    {
        byte[] {Length:16} bytes => new Guid(bytes),
        string value when Guid.TryParse(value,out var guid) => guid,
        _ => Guid.Empty
    };
}
