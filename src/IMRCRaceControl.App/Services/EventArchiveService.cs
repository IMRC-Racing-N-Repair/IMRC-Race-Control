using System.IO;
using System.Text.Json;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Services;

public sealed class EventArchiveService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string ArchiveDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IMRCRaceControl",
        "EventArchive");

    public async Task<string> SaveAsync(RaceDayEventDefinition raceEvent)
    {
        Directory.CreateDirectory(ArchiveDirectory);

        var date = raceEvent.EventDate.ToString("yyyyMMdd");
        var completed = (raceEvent.CompletedAt ?? DateTimeOffset.Now).ToString("yyyyMMdd_HHmmss");
        var safeName = MakeSafeFilePart(raceEvent.Name);
        var shortId = raceEvent.Id.Length > 8 ? raceEvent.Id[..8] : raceEvent.Id;
        var path = Path.Combine(ArchiveDirectory, $"{date}_{safeName}_{completed}_{shortId}.json");

        var envelope = new
        {
            archiveVersion = 1,
            applicationVersion = "0.5.0",
            archivedAt = DateTimeOffset.Now,
            raceEvent
        };

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, envelope, JsonOptions);
        return path;
    }

    private static string MakeSafeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((value ?? "Race Day").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }
}
