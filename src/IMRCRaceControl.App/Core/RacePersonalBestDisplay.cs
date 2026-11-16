using System.Globalization;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

/// <summary>
/// Pure race-result PB projection: never equate one fast lap with a completed
/// race PB. Live predictions are advisory; only completed results are records.
/// </summary>
public static class RacePersonalBestDisplay
{
    public sealed record State(string Text, bool IsNewRecord);

    public static State Evaluate(DriverPersonalBest? previous, RaceFormat format,
        int distance, int laps, double elapsedSeconds, bool finished)
    {
        if (distance <= 0 || laps < 1 || !double.IsFinite(elapsedSeconds) ||
            elapsedSeconds <= 0) return new("", false);
        if (finished)
        {
            if (!PersonalBests.Beats(previous,laps,elapsedSeconds,format,distance))
                return new("",false);
            return new($"PB: {laps}/{FormatSeconds(elapsedSeconds)}",true);
        }

        // No earlier reference means no meaningful "beating PB pace" indicator.
        if (previous is null || previous.Format != format || previous.Distance != distance)
            return new("",false);
        if (format == RaceFormat.Laps && laps >= distance)
            return new("",false);

        // Use average elapsed time per completed lap as a simple race-distance
        // pace projection. This is not an official PB until the racer finishes.
        var perLap = elapsedSeconds / laps;
        if (perLap <= 0) return new("",false);
        if (format == RaceFormat.Laps)
        {
            var predicted = perLap * distance;
            return PersonalBests.Beats(previous,distance,predicted,format,distance)
                ? new($"PB: {previous.Laps}/{FormatSeconds(previous.TotalSeconds)}",false)
                : new("",false);
        }

        var projectedLaps = (int)Math.Floor(distance / perLap);
        if (projectedLaps <= 0) return new("",false);
        // For matching lap totals, completion time is unknown until the race
        // ends, so only claim ahead-of-PB pace for strictly more projected laps.
        return projectedLaps > previous.Laps
            ? new($"PB: {previous.Laps}/{FormatSeconds(previous.TotalSeconds)}",false)
            : new("",false);
    }

    private static string FormatSeconds(double seconds) =>
        TimeSpan.FromSeconds(seconds).ToString(@"m\:ss\.fff",CultureInfo.InvariantCulture);
}
