namespace IMRCRaceControl.Models;

public sealed class PracticeHistoryLapRow
{
    public int LapNumber { get; init; }
    public double LapTimeSeconds { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string Source { get; init; } = string.Empty;

    public string LapTimeDisplay => LapTimeSeconds.ToString("0.000");
    public string TimeDisplay => Timestamp.LocalDateTime.ToString("h:mm:ss.fff tt");
}
