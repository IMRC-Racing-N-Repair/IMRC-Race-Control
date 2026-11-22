namespace IMRCRaceControl.Models;

public sealed class PracticeLapEntry
{
    public int LapNumber { get; init; }
    public TimeSpan LapTime { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string Source { get; init; } = string.Empty;

    public TimeSpan? PreviousLapDelta { get; internal set; }
    private decimal? RoundedDelta => PreviousLapDelta is { } delta
        ? Math.Round((decimal)delta.Ticks / TimeSpan.TicksPerSecond, 3, MidpointRounding.AwayFromZero)
        : null;
    public string LapDeltaDisplay => RoundedDelta is { } value
        ? value.ToString("+0.000;-0.000;0.000", System.Globalization.CultureInfo.InvariantCulture)
        : "—";
    public bool IsFaster => RoundedDelta is < 0;
    public bool IsSlower => RoundedDelta is > 0;

    public string LapTimeDisplay => LapTime.TotalMinutes >= 1
        ? $"{(int)LapTime.TotalMinutes}:{LapTime.Seconds:00}.{LapTime.Milliseconds:000}"
        : $"{LapTime.TotalSeconds:0.000}";
}
