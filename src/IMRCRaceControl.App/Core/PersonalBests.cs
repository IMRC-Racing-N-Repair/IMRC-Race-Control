using System.Globalization;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

/// <summary>
/// Persistent fastest known legal lap for one stable driver/class identity.
/// This is venue-independent; the track of the winning lap is retained for display.
/// </summary>
public sealed record DriverPersonalBest(
    string DriverId, string ClassId, double LapSeconds, DateTimeOffset AchievedAt,
    string TrackId, string EventId, string SessionId, string Source)
{
    public string TimeDisplay => LapSeconds.ToString("0.000", CultureInfo.InvariantCulture);
}

public static class PersonalBests
{
    // A PB is a strictly faster positive lap, not merely a new race entry.
    public static bool Beats(DriverPersonalBest? previous, double seconds) =>
        double.IsFinite(seconds) && seconds > 0 && seconds < 86400 &&
        (previous is null || seconds < previous.LapSeconds - 0.0000001);

    /// <summary>
    /// Derive from completed race histories. Imported source summary best-lap
    /// values are used when individual crossings were not supplied, including
    /// results-only imports. Unmapped drivers/classes are excluded.
    /// </summary>
    public static List<DriverPersonalBest> FromEvents(IEnumerable<RaceDayEventDefinition> events)
    {
        var best = new Dictionary<(string Driver, string Class), DriverPersonalBest>();
        foreach (var ev in events)
        foreach (var session in ev.Sessions)
        {
            if (session.ProgressStatus != EventSessionStatus.Completed ||
                session.SessionType is not (SessionType.Qualifying or SessionType.Race) ||
                string.IsNullOrWhiteSpace(session.ClassId)) continue;

            foreach (var entrant in session.Entrants)
            {
                if (string.IsNullOrWhiteSpace(entrant.DriverId)) continue;
                var key = (entrant.DriverId, session.ClassId);
                var previous = best.GetValueOrDefault(key);
                var laps = session.Laps.Where(l => l.CarNumber == entrant.CarNumber &&
                        l.LapNumber > 0 && l.LapTime > TimeSpan.Zero &&
                        l.LapTime.TotalSeconds < 86400)
                    .OrderBy(l => l.LapTime).ThenBy(l => l.Timestamp).FirstOrDefault();
                var summary = session.Results.FirstOrDefault(x => x.CarNumber == entrant.CarNumber);
                var summarySeconds = ParseSeconds(summary?.BestLap);
                var lapSeconds = laps?.LapTime.TotalSeconds ?? double.PositiveInfinity;
                // The official summary can contain the personal-best lap even when
                // the original source omitted the individual lap records.
                var seconds = Math.Min(lapSeconds, summarySeconds);
                if (!Beats(previous, seconds)) continue;
                var timestamp = laps is not null && lapSeconds <= summarySeconds
                    ? laps.Timestamp : session.CompletedAt ?? ev.CompletedAt ?? ev.CreatedAt;
                best[key] = new(entrant.DriverId, session.ClassId, seconds, timestamp,
                    ev.Track?.Id ?? "", ev.Id, session.Id,
                    laps is not null && lapSeconds <= summarySeconds ? "Scored lap" : "Race result summary");
            }
        }
        return best.Values.OrderBy(p => p.DriverId, StringComparer.Ordinal)
            .ThenBy(p => p.ClassId, StringComparer.Ordinal).ToList();
    }

    public static DriverPersonalBest? Lookup(IEnumerable<DriverPersonalBest> records,
        string driverId, string classId) =>
        records.FirstOrDefault(x => x.DriverId == driverId && x.ClassId == classId);

    private static double ParseSeconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return double.PositiveInfinity;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return seconds > 0 && seconds < 86400 ? seconds : double.PositiveInfinity;
        // Result displays may contain m:ss.fff; do not interpret placeholder text.
        var parts = text.Split(':');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var remainder) &&
            minutes >= 0 && remainder >= 0 && remainder < 60)
            return minutes * 60 + remainder;
        return double.PositiveInfinity;
    }
}
