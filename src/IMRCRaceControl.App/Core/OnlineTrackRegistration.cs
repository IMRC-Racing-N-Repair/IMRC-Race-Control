using System.Text.Json;
using IMRCRaceControl.Models;
namespace IMRCRaceControl.Core;
public static class OnlineTrackRegistration
{
    public static string BuildBody(TrackProfile track)
    {
        if (!Guid.TryParse(track.Id, out var uuid) || uuid == Guid.Empty) throw new ArgumentException("Save a valid track UUID first.");
        if (string.IsNullOrWhiteSpace(track.Name)) throw new ArgumentException("Save your track name in Track Info first.");
        var name = track.Name.Trim();
        var location = string.Join(", ", new[] {track.City, track.Region, track.Country}.Where(s => !string.IsNullOrWhiteSpace(s)));
        var surface = string.Join(" / ", new[] {track.TrackType, track.Surface}.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (name.Length > 120 || location.Length > 160 || surface.Length > 80) throw new ArgumentException("Track name, location or surface is too long for the website.");
        return JsonSerializer.Serialize(new {uuid = uuid.ToString("N"), name, location, surface});
    }
}
