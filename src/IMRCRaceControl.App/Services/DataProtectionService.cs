using Microsoft.Data.Sqlite;

namespace IMRCRaceControl.Services;

public static class DataProtectionService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<string, DateTimeOffset> LastBackup = new(StringComparer.OrdinalIgnoreCase);

    public static Task PrepareAsync() => PrepareAsync(ApplicationDataPaths.DataDirectory,
        ApplicationDataPaths.LegacyDirectory, ApplicationDataPaths.BackupDirectory);

    public static async Task PrepareAsync(string dataDirectory, string legacyDirectory, string backupDirectory)
    {
        await Gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(dataDirectory); Directory.CreateDirectory(backupDirectory);
            var database = Path.Combine(dataDirectory,"RaceControl.db");
            var pending = Path.Combine(backupDirectory,"PendingRestore.db");
            if (File.Exists(pending))
            {
                // Validate the replacement before touching the current data. Retain the current data too.
                await ValidateAsync(pending);
                if (File.Exists(database)) await SnapshotCoreAsync(database,backupDirectory,"before-restore");
                await ReplaceFromDatabaseAsync(pending,database);
                File.Move(pending,Path.Combine(backupDirectory,$"Restored-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}.db"));
            }
            if (!File.Exists(database))
            {
                var legacy = Path.Combine(legacyDirectory,"RaceControl.db");
                if (File.Exists(legacy))
                {
                    await ReplaceFromDatabaseAsync(legacy,database);
                    await SnapshotCoreAsync(database,backupDirectory,"migration");
                }
                else
                {
                    // A missing live database must not cause the last valid backup to be replaced by defaults.
                    var backups = Directory.GetFiles(backupDirectory,"RaceControl-*.db")
                        .OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
                    Exception? lastError = null;
                    foreach (var backup in backups)
                    {
                        try { await ReplaceFromDatabaseAsync(backup,database); lastError = null; break; }
                        catch (Exception ex) { lastError = ex; }
                    }
                    if (!File.Exists(database) && lastError is not null)
                        throw new IOException("Saved data is missing and the available database backups could not be read. No empty database was created.",lastError);
                }
            }
            var oldSettings = Path.Combine(legacyDirectory,"settings.json");
            var newSettings = Path.Combine(dataDirectory,"settings.json");
            if (!File.Exists(newSettings) && File.Exists(oldSettings))
            {
                // Preserve the original. Invalid settings are reported at load time, never erased.
                File.Copy(oldSettings,newSettings,false);
            }
            foreach (var folder in new[] { "Audio", "Results", "EventArchive", "OnlineOutbox", "Logs" })
                CopyUserFolder(Path.Combine(legacyDirectory,folder),Path.Combine(dataDirectory,folder));
        }
        finally { Gate.Release(); }
    }

    private static void CopyUserFolder(string source,string target)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(target);
        foreach (var path in Directory.GetFiles(source))
        {
            var destination = Path.Combine(target,Path.GetFileName(path));
            if (!File.Exists(destination)) File.Copy(path,destination,false);
        }
        foreach (var path in Directory.GetDirectories(source))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                CopyUserFolder(path,Path.Combine(target,Path.GetFileName(path)));
    }

    public static async Task<string?> BackupAsync(string database,string backups,bool force = false)
    {
        await Gate.WaitAsync();
        try
        {
            if (!File.Exists(database)) return null;
            if (!force && LastBackup.TryGetValue(database,out var last) && DateTimeOffset.UtcNow-last < TimeSpan.FromMinutes(10)) return null;
            var result = await SnapshotCoreAsync(database,backups,"automatic");
            LastBackup[database] = DateTimeOffset.UtcNow;
            return result;
        }
        finally { Gate.Release(); }
    }

    public static async Task QueueRestoreAsync(string source,string backups)
    {
        await Gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(backups);
            var target = Path.Combine(backups,"PendingRestore.db");
            await ReplaceFromDatabaseAsync(source,target);
        }
        finally { Gate.Release(); }
    }

    private static async Task<string> SnapshotCoreAsync(string database,string backups,string reason)
    {
        Directory.CreateDirectory(backups);
        var path = Path.Combine(backups,$"RaceControl-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{reason}.db");
        await ReplaceFromDatabaseAsync(database,path);
        return path;
    }

    private static async Task ReplaceFromDatabaseAsync(string source,string target)
    {
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(target),StringComparison.OrdinalIgnoreCase))
            throw new IOException("Select a backup different from the current database.");
        await ValidateAsync(source);
        var temp = target+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            // SQLite's online backup includes committed WAL pages. Never copy a live .db by itself.
            await using (var from = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=source, Mode=SqliteOpenMode.ReadOnly, Pooling=false }.ToString()))
            await using (var to = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=temp, Mode=SqliteOpenMode.ReadWriteCreate, Pooling=false }.ToString()))
            {
                await from.OpenAsync(); await to.OpenAsync(); from.BackupDatabase(to);
            }
            await ValidateAsync(temp);
            // Startup owns replacement; no application connections have been opened yet.
            // A restore replaces the primary file only after a safe snapshot was made by PrepareAsync.
            // Remove stale WAL sidecars at startup so they cannot replay over the restored snapshot.
            if (File.Exists(target))
            {
                await using var existing = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=target, Mode=SqliteOpenMode.ReadWrite, Pooling=false }.ToString());
                await existing.OpenAsync();
                await using var checkpoint = existing.CreateCommand(); checkpoint.CommandText="PRAGMA wal_checkpoint(TRUNCATE);";
                await using var result = await checkpoint.ExecuteReaderAsync();
                if (await result.ReadAsync() && result.GetInt32(0) != 0)
                    throw new IOException("The database is busy. Close other copies of Race Control before restoring.");
            }
            File.Move(temp,target,true);
            foreach (var suffix in new[] { "-wal", "-shm" })
                if (File.Exists(target+suffix)) File.Delete(target+suffix);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static async Task ValidateAsync(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new IOException("The selected database is missing or empty.");
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path, Mode=SqliteOpenMode.ReadOnly, Pooling=false }.ToString());
        await connection.OpenAsync();
        await using var check = connection.CreateCommand(); check.CommandText="PRAGMA quick_check;";
        if (!string.Equals(await check.ExecuteScalarAsync() as string,"ok",StringComparison.OrdinalIgnoreCase))
            throw new IOException("Database integrity check failed. The database was left unchanged.");
        await using var table = connection.CreateCommand(); table.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('AppState','PracticeStints');";
        if (Convert.ToInt64(await table.ExecuteScalarAsync()) == 0) throw new IOException("This is not an IMRC Race Control database.");
    }
}
