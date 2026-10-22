namespace IMRCRaceControl.Models;

public sealed class DriverLapViewRow
{
    public long Sequence { get; init; }
    public int CarNumber { get; init; }
    public int LapNumber { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string LapLabel { get; init; } = string.Empty;
    public string LapTime { get; init; } = "—";
    public string Elapsed { get; init; } = "—";
    public string Status { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public bool IsBreakout { get; init; }

    public string RecordKey => IsBreakout ? $"B:{Sequence}" : $"L:{Sequence}";
}
