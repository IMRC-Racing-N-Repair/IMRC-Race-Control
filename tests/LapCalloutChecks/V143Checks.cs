using System.Text.Json;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;

static class V143Checks
{
    public static async Task<int> Run(bool isolated)
    {
        int count = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); count++; }
        var legacy = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Check(Guid.TryParse(legacy.Track.Id, out _) && legacy.Track.Name == "", "Legacy settings receive an unnamed track identity");
        var oldEvent = JsonSerializer.Deserialize<RaceDayEventDefinition>("{}")!;
        Check(oldEvent.Track is null, "Old events are not silently assigned to a track");
        var track = new TrackProfile { Name = "Riverside", City = "Warren", TimeZoneId = "America/New_York" };
        var ev = new RaceDayEventDefinition { Track = track.Snapshot() };
        track.Name = "Updated track name";
        Check(ev.Track.Name == "Riverside" && ev.Track.Id == track.Id, "Track edits preserve event history and identity");
        var settings = new AppSettings { Track = track, Events = [ev, oldEvent] };
        var clone = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Check(clone.Track.Id == track.Id && clone.Events[0].Track!.Id == track.Id && clone.Events[1].Track is null, "Track identity survives backup serialization");
        if (isolated)
        {
            if (!Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).StartsWith("/tmp/imrc-callout-test-data")) throw new Exception("Isolated data directory required");
            var db = new RaceControlDatabaseService(); await db.InitializeAsync(); await db.SaveApplicationStateAsync(settings);
            var restored = (await db.LoadApplicationStateAsync())!;
            Check(restored.Track.Id == track.Id && restored.Track.Name == "Updated track name" && restored.Events[0].Track!.Name == "Riverside" && restored.Events[0].Track!.TimeZoneId == "America/New_York", "SQLite preserves track identity and separate historical snapshot");
        }
        return count;
    }
}
