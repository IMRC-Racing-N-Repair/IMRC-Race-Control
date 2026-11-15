using System.Globalization;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

/// <summary>
/// Best overall race result for an individual driver/class, grouped by race
/// format and distance. Timed events compare laps then elapsed completion time;
/// fixed-lap events compare elapsed time after reaching the target.
/// Fastest single lap is intentionally NOT the personal best metric.
/// </summary>
public sealed record DriverPersonalBest(
    string DriverId, string ClassId, RaceFormat Format, int Distance,
    int Laps, double TotalSeconds, DateTimeOffset AchievedAt,
    string TrackId, string EventId, string SessionId, string Source)
{
    public string ResultDisplay => Format == RaceFormat.Timed
        ? $"{Laps} laps / {FormatTime(TotalSeconds)}"
        : $"{Distance} laps / {FormatTime(TotalSeconds)}";

    public string DistanceDisplay => Format == RaceFormat.Timed
        ? $"{Distance / 60.0:0.##} min" : $"{Distance} laps";

    private static string FormatTime(double seconds) =>
        TimeSpan.FromSeconds(seconds).ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
}

public static class PersonalBests
{
    // Equal results are not a new PB. Comparison is valid only within the
    // same driver, class, format and configured distance.
    public static bool Beats(DriverPersonalBest? previous, int laps, double elapsedSeconds,
        RaceFormat format, int distance)
    {
        if (distance <= 0 || laps <= 0 || !double.IsFinite(elapsedSeconds) ||
            elapsedSeconds <= 0 || elapsedSeconds >= 604800) return false;
        if (format == RaceFormat.Laps && laps < distance) return false;
        if (previous is null) return true;
        if (previous.Format != format || previous.Distance != distance)
            return false;
        return format == RaceFormat.Timed
            ? laps > previous.Laps || laps == previous.Laps && elapsedSeconds < previous.TotalSeconds - 0.0000001
            : elapsedSeconds < previous.TotalSeconds - 0.0000001;
    }

    public static List<DriverPersonalBest> FromEvents(IEnumerable<RaceDayEventDefinition> events)
    {
        var best = new Dictionary<(string Driver, string Class, RaceFormat Format, int Distance), DriverPersonalBest>();
        foreach (var ev in events)
        foreach (var session in ev.Sessions)
        {
            if (session.ProgressStatus != EventSessionStatus.Completed ||
                session.SessionType is not (SessionType.Qualifying or SessionType.Race) ||
                string.IsNullOrWhiteSpace(session.ClassId)) continue;

            // Unknown race length is NOT assumed to be five minutes or 50 laps.
            // Imported sources must provide actual race rules to qualify for PBs.
            var distance = session.RaceFormat == RaceFormat.Timed
                ? session.RaceMinutesOverride is > 0 and < 10080
                    ? (int)Math.Round(session.RaceMinutesOverride.Value * 60, MidpointRounding.AwayFromZero) : 0
                : session.TargetLaps > 0 ? session.TargetLaps : 0;
            if (distance == 0) continue;

            foreach (var entrant in session.Entrants)
            {
                if (string.IsNullOrWhiteSpace(entrant.DriverId)) continue;
                var result = session.Results.FirstOrDefault(x => x.CarNumber == entrant.CarNumber);
                if (result is null || result.Laps <= 0 ||
                    result.Status is "DNS" or "DNF" or "DQ") continue;
                var seconds = ParseTime(result.TotalTime);
                var key = (entrant.DriverId, session.ClassId, session.RaceFormat, distance);
                var prior = best.GetValueOrDefault(key);
                if (!Beats(prior, result.Laps, seconds, session.RaceFormat, distance)) continue;
                best[key] = new(entrant.DriverId, session.ClassId, session.RaceFormat,
                    distance, result.Laps, seconds,
                    session.CompletedAt ?? ev.CompletedAt ?? ev.CreatedAt,
                    ev.Track?.Id ?? "", ev.Id, session.Id, "Completed race result");
            }
        }
        return best.Values.OrderBy(p => p.DriverId, StringComparer.Ordinal)
            .ThenBy(p => p.ClassId, StringComparer.Ordinal)
            .ThenBy(p => p.Format).ThenBy(p => p.Distance).ToList();
    }

    public static DriverPersonalBest? Lookup(IEnumerable<DriverPersonalBest> records,
        string driverId, string classId, RaceFormat format, int distance) =>
        records.FirstOrDefault(x => x.DriverId == driverId && x.ClassId == classId &&
            x.Format == format && x.Distance == distance);

    private static double ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim() == "—") return double.NaN;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return seconds > 0 ? seconds : double.NaN;
        var parts = value.Split(':');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var remainder) &&
            minutes >= 0 && remainder >= 0 && remainder < 60)
            return minutes * 60 + remainder;
        if (parts.Length == 3 &&
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var mins) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var sec) &&
            hours >= 0 && mins is >= 0 and < 60 && sec is >= 0 and < 60)
            return hours * 3600 + mins * 60 + sec;
        return double.NaN;
    }
}
