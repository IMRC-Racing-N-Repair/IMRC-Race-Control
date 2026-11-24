using System.Globalization;

namespace IMRCRaceControl.Core;

/// <summary>
/// Formats the projected difference to the best qualifier. Comparisons across
/// different lap counts must not pretend to be elapsed-time gaps.
/// </summary>
public static class QualifyingPaceGap
{
    public static string BehindLeader(int leaderLaps, TimeSpan? leaderTime, int racerLaps, TimeSpan? racerTime)
    {
        if (leaderLaps <= 0 || racerLaps <= 0) return "—";
        var lapDifference = leaderLaps - racerLaps;
        if (lapDifference > 0) return $"+{lapDifference} LAP{(lapDifference == 1 ? "" : "S")}";
        if (lapDifference < 0) return "—";
        if (leaderTime is null || racerTime is null) return "—";
        return FormatSigned(Math.Max(0, (racerTime.Value - leaderTime.Value).TotalSeconds), "+");
    }

    // A leader only shows a negative time if currently faster than the last
    // established TQ (the best completed prior qualifying run).
    public static string LeaderVsPreviousTq(
        int leaderLaps, TimeSpan? leaderTime, int priorTqLaps, TimeSpan? priorTqTime)
    {
        if (leaderLaps <= 0 || priorTqLaps <= 0) return "—";
        if (leaderLaps > priorTqLaps)
            return $"-{leaderLaps - priorTqLaps} LAP{(leaderLaps - priorTqLaps == 1 ? "" : "S")}";
        if (leaderLaps < priorTqLaps || leaderTime is null || priorTqTime is null) return "—";
        var improvement = (priorTqTime.Value - leaderTime.Value).TotalSeconds;
        return improvement > 0.0005 ? FormatSigned(improvement, "-") : "—";
    }

    private static string FormatSigned(double seconds, string prefix) =>
        prefix + seconds.ToString("0.000", CultureInfo.InvariantCulture);
}
