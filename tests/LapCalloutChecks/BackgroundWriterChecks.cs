using IMRCRaceControl.Models;
using IMRCRaceControl.Services;
using System.Text.Json;

internal static class BackgroundWriterChecks
{
    public static async Task<int> Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"imrc-writer-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var db=new RaceControlDatabaseService(
                Path.Combine(root,"race.db"),Path.Combine(root,"Backups"));
            var settingsService=new SettingsService(Path.Combine(root,"settings.json"));
            await using(var writer=new BackgroundRaceStateWriter(db,settingsService))
            {
                var first=new AppSettings { Classes=[new ClassDefinition {Id="cls",Name="First"}] };
                var second=new AppSettings { Classes=[new ClassDefinition {Id="cls",Name="Second"}] };
                var third=new AppSettings { Classes=[new ClassDefinition {Id="cls",Name="Third"}] };
                var a=writer.WriteAsync(JsonSerializer.Serialize(first));
                var c=writer.WriteAsync(JsonSerializer.Serialize(second));
                var d=writer.WriteAsync(JsonSerializer.Serialize(third));
                await Task.WhenAll(a,c,d);
                var persisted=await db.LoadApplicationStateAsync();
                if(persisted?.Classes.Single().Name!="Third")
                    throw new Exception("Background writer did not preserve save order");
                var restored=await settingsService.LoadAsync();
                if(restored.Classes.Single().Name!="Third")
                    throw new Exception("Settings recovery file is out of date");
            }
            // A new writer can continue from the previous database without
            // corrupting or replacing an existing completed write.
            return 2;
        }
        finally { try { Directory.Delete(root,true); } catch { } }
    }
}
