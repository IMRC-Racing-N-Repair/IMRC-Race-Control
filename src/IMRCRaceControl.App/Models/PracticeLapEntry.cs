namespace IMRCRaceControl.Models;

public sealed class PracticeLapEntry
{
    public int LapNumber { get; init; }
    public TimeSpan LapTime { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string Source { get; init; } = string.Empty;

    public string LapTimeDisplay => LapTime.TotalMinutes >= 1
        ? $"{(int)LapTime.TotalMinutes}:{LapTime.Seconds:00}.{LapTime.Milliseconds:000}"
        : $"{LapTime.TotalSeconds:0.000}";
}
