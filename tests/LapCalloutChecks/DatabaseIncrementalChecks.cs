using IMRCRaceControl.Models;
using IMRCRaceControl.Services;
using Microsoft.Data.Sqlite;

internal static class DatabaseIncrementalChecks
{
    public static async Task<int> Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "imrc-incremental-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "race.db");
            var db = new RaceControlDatabaseService(path, Path.Combine(root, "backups"));
            var cls1 = new ClassDefinition { Id = "stock", Name = "Stock" };
            var cls2 = new ClassDefinition { Id = "pro", Name = "Pro" };
            static RaceDayEventDefinition MakeEvent(string id, int number) => new()
            {
                Id = id, Name = id, Sessions =
                [
                    new EventSessionDefinition
                    {
                        Id = "session-" + id, Name = id, ClassId = "stock",
                        ClassName = "Stock", ProgressStatus = EventSessionStatus.Completed,
                        Laps =
                        [
                            new LapRecord(1,number,"Driver",1,TimeSpan.FromSeconds(5.123),
                                TimeSpan.FromSeconds(5.123),
                                DateTimeOffset.Parse("2026-11-15T15:00:00Z"),"test",false)
                        ]
                    }
                ]
            };
            var a = MakeEvent("race-day-1",1);
            var c = MakeEvent("race-day-2",2);
            var settings = new AppSettings { Classes = [cls1,cls2], Events = [a,c] };
            await db.SaveApplicationStateAsync(settings);
            async Task<long> Row(string table, string field, string id)
            {
                await using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT rowid FROM {table} WHERE {field} = $id LIMIT 1";
                cmd.Parameters.AddWithValue("$id", id);
                return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L);
            }
            var lap1 = await Row("EventSessionLaps","EventId",a.Id);
            var lap2 = await Row("EventSessionLaps","EventId",c.Id);
            var class2 = await Row("Classes","Id",cls2.Id);
            if(lap1==0 || lap2==0 || class2==0)
                throw new Exception("Fixture was not persisted.");
            // A trigger observes DELETE operations even when SQLite reuses rowids.
            await using (var audit = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await audit.OpenAsync();
                await using var cmd = audit.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE AuditLapDeletes(EventId TEXT NOT NULL);
                    CREATE TRIGGER log_lap_removal AFTER DELETE ON EventSessionLaps
                    BEGIN INSERT INTO AuditLapDeletes(EventId) VALUES(old.EventId); END;
                    """;
                await cmd.ExecuteNonQueryAsync();
            }
            async Task<string[]> DeletedEvents()
            {
                await using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT EventId FROM AuditLapDeletes";
                var result = new List<string>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) result.Add(reader.GetString(0));
                return result.ToArray();
            }
            cls1.Name = "Stock Updated";
            await db.SaveApplicationStateAsync(settings);
            if(lap1!=await Row("EventSessionLaps","EventId",a.Id) ||
               lap2!=await Row("EventSessionLaps","EventId",c.Id) ||
               class2!=await Row("Classes","Id",cls2.Id))
                throw new Exception("Class edit rewrote unchanged classes or historical laps.");
            if ((await DeletedEvents()).Length != 0)
                throw new Exception("Editing only class settings deleted historic lap rows.");
            a.Sessions[0].Laps.Add(new LapRecord(2,1,"Driver",2,TimeSpan.FromSeconds(5.1),
                TimeSpan.FromSeconds(10.223),
                DateTimeOffset.Parse("2026-11-15T15:00:06Z"),"test",false));
            await db.SaveApplicationStateAsync(settings);
            if(lap2 != await Row("EventSessionLaps","EventId",c.Id) ||
               class2 != await Row("Classes","Id",cls2.Id) ||
               (await DeletedEvents()).Any(id => id != a.Id))
                throw new Exception("One-event edit changed unrelated events or classes.");
            var loaded = await db.LoadApplicationStateAsync();
            if(loaded is null || loaded.Events.Count!=2 ||
                loaded.Events.Single(e=>e.Id==a.Id).Sessions[0].Laps.Count!=2 ||
                loaded.Events.Single(e=>e.Id==c.Id).Sessions[0].Laps.Count!=1 ||
                loaded.Classes.Single(e=>e.Id==cls1.Id).Name!="Stock Updated")
                throw new Exception("Incremental save failed round-trip integrity.");
            // Checkbox membership edits must not delete or reconstruct historic
            // laps, but must update both authoritative snapshot and query tables.
            var deletesBefore = (await DeletedEvents()).Length;
            var beforePatchLap1 = await Row("EventSessionLaps", "EventId", a.Id);
            var beforePatchLap2 = await Row("EventSessionLaps", "EventId", c.Id);
            await db.UpdateEventClassIdsAsync(a.Id, System.Text.Json.JsonSerializer.Serialize(new[] { "stock", "pro" }));
            var membership = await db.LoadApplicationStateAsync();
            var selectedCount = membership?.Events.Single(e => e.Id == a.Id).ClassIds.Count ?? -1;
            var otherCount = membership?.Events.Single(e => e.Id == c.Id).ClassIds.Count ?? -1;
            var afterDeletes = (await DeletedEvents()).Length;
            var afterLap1 = await Row("EventSessionLaps","EventId",a.Id);
            var afterLap2 = await Row("EventSessionLaps","EventId",c.Id);
            if (selectedCount != 2 || otherCount != 0 ||
                afterDeletes != deletesBefore || beforePatchLap1 != afterLap1 || beforePatchLap2 != afterLap2)
                throw new Exception($"Targeted update mismatch: changed={selectedCount}, other={otherCount}, deleted={deletesBefore}->{afterDeletes}, laps={beforePatchLap1}->{afterLap1} and {beforePatchLap2}->{afterLap2}.");
            await using (var verify = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False"))
            {
                await verify.OpenAsync();
                await using var cmd = verify.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM EventClasses WHERE EventId=$id";
                cmd.Parameters.AddWithValue("$id", a.Id);
                if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) != 2)
                    throw new Exception("Targeted membership update did not synchronize EventClasses.");
            }
            // Regression: editing driver registration must not delete or
            // recreate results from unrelated historical races.
            var originalDeletes=(await DeletedEvents()).Length;
            var oldLap1=await Row("EventSessionLaps","EventId",a.Id);
            var oldLap2=await Row("EventSessionLaps","EventId",c.Id);
            var registration=new EventDriverAssignment {
                Id="registration-1",DriverId="driver-1",ClassId="stock",TransponderId="1234567"
            };
            await db.UpdateEventRegistrationAsync(a.Id,
                System.Text.Json.JsonSerializer.Serialize(new[]{"stock","pro"}),
                System.Text.Json.JsonSerializer.Serialize(new[]{registration}),"[]");
            var reloaded=await db.LoadApplicationStateAsync();
            if(reloaded?.Events.Single(e=>e.Id==a.Id).DriverAssignments.Count!=1 ||
                reloaded.Events.Single(e=>e.Id==a.Id).DriverAssignments[0].TransponderId!="1234567" ||
                reloaded.Events.Single(e=>e.Id==c.Id).DriverAssignments.Count!=0 ||
                (await DeletedEvents()).Length!=originalDeletes ||
                await Row("EventSessionLaps","EventId",a.Id)!=oldLap1 ||
                await Row("EventSessionLaps","EventId",c.Id)!=oldLap2)
                throw new Exception("Targeted registration edit changed historical results or failed to persist.");
            return 6;
        }
        finally { try { Directory.Delete(root,true); } catch { } }
    }
}
