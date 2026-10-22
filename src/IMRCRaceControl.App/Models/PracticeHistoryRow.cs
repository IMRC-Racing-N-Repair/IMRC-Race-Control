namespace IMRCRaceControl.Models;

public sealed class PracticeHistoryRow
{
    public string StintId { get; init; } = string.Empty;
    public string DriverId { get; init; } = string.Empty;
    public string DriverName { get; init; } = string.Empty;
    public string TransponderId { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    public int LapCount { get; init; }
    public double? BestLapSeconds { get; init; }
    public double? AverageLapSeconds { get; init; }

    public string DateDisplay => StartedAt.LocalDateTime.ToString("MM/dd/yyyy");
    public string TimeDisplay => $"{StartedAt.LocalDateTime:h:mm:ss tt} – {EndedAt.LocalDateTime:h:mm:ss tt}";
    public string BestLapDisplay => BestLapSeconds.HasValue ? BestLapSeconds.Value.ToString("0.000") : "—";
    public string AverageLapDisplay => AverageLapSeconds.HasValue ? AverageLapSeconds.Value.ToString("0.000") : "—";
}
