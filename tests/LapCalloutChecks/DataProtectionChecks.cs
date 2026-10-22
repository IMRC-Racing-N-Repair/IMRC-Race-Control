using IMRCRaceControl.Services;
using Microsoft.Data.Sqlite;

internal static class DataProtectionChecks
{
    public static async Task<int> Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"imrc-retention-"+Guid.NewGuid().ToString("N"));
        var legacy=Path.Combine(root,"old-install"); var data=Path.Combine(root,"user-data"); var backups=Path.Combine(root,"backups");
        Directory.CreateDirectory(legacy); var checks=0;
        void Check(bool value,string label) { if(!value) throw new Exception("Data protection: "+label); checks++; }
        async Task<string> Read(string file)
        {
            await using var c=new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False"); await c.OpenAsync();
            await using var q=c.CreateCommand(); q.CommandText="SELECT Payload FROM AppState"; return (string)(await q.ExecuteScalarAsync())!;
        }
        async Task Write(string file,string value)
        {
            await using var c=new SqliteConnection($"Data Source={file};Pooling=False"); await c.OpenAsync();
            await using var q=c.CreateCommand(); q.CommandText="UPDATE AppState SET Payload=$p"; q.Parameters.AddWithValue("$p",value); await q.ExecuteNonQueryAsync();
        }
        try
        {
            var old=Path.Combine(legacy,"RaceControl.db"); var current=Path.Combine(data,"RaceControl.db");
            await using(var live=new SqliteConnection($"Data Source={old};Pooling=False"))
            {
                await live.OpenAsync(); await using var q=live.CreateCommand();
                q.CommandText="PRAGMA journal_mode=WAL; CREATE TABLE AppState(Payload TEXT); INSERT INTO AppState VALUES('saved event');"; await q.ExecuteNonQueryAsync();
                await File.WriteAllTextAsync(Path.Combine(legacy,"settings.json"),"original settings");
                Directory.CreateDirectory(Path.Combine(legacy,"Audio")); await File.WriteAllTextAsync(Path.Combine(legacy,"Audio","start.wav"),"audio");
                await DataProtectionService.PrepareAsync(data,legacy,backups);
                Check(await Read(current)=="saved event","migration includes committed WAL with original connection open");
                Check(File.Exists(old),"migration preserves source database");
            }
            Directory.Delete(legacy,true); // Simulate repair deleting the entire old install root.
            await DataProtectionService.PrepareAsync(data,legacy,backups);
            Check(await Read(current)=="saved event","repair retains events");
            Check(await File.ReadAllTextAsync(Path.Combine(data,"settings.json"))=="original settings","repair retains settings");
            Check(File.Exists(Path.Combine(data,"Audio","start.wav")),"repair retains imported tones");
            var snapshot=(await DataProtectionService.BackupAsync(current,backups,true))!;
            await Write(current,"new event");
            await DataProtectionService.QueueRestoreAsync(snapshot,backups);
            Check(await Read(current)=="new event","queued restore does not replace live database");
            await DataProtectionService.PrepareAsync(data,legacy,backups);
            Check(await Read(current)=="saved event","restart restores selected snapshot");
            var before=Directory.GetFiles(backups,"*before-restore.db").Single();
            Check(await Read(before)=="new event","restore retains replaced data in safety backup");
            var corrupt=Path.Combine(root,"bad.db"); await File.WriteAllTextAsync(corrupt,"invalid database");
            var rejected=false;
            try { await DataProtectionService.QueueRestoreAsync(corrupt,backups); } catch { rejected=true; }
            Check(rejected && !File.Exists(Path.Combine(backups,"PendingRestore.db")),"invalid restore is rejected");
            Check(await Read(current)=="saved event","invalid restore leaves data intact");
            File.Delete(current);
            await DataProtectionService.PrepareAsync(data,legacy,backups);
            Check(await Read(current)=="new event","missing live database recovers latest valid safety snapshot");
            Directory.Delete(data,true); Directory.Delete(backups,true); Directory.CreateDirectory(backups);
            await File.WriteAllTextAsync(Path.Combine(backups,"RaceControl-bad.db"),"invalid");
            rejected=false;
            try { await DataProtectionService.PrepareAsync(data,legacy,backups); } catch { rejected=true; }
            Check(rejected && !File.Exists(current),"invalid backups fail safely without creating empty database");
            Check(!ApplicationDataPaths.DataDirectory.StartsWith(ApplicationDataPaths.LegacyDirectory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"user data is outside old installation");
            return checks;
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
