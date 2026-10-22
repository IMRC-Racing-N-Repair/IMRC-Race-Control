using IMRCRaceControl.Core;

namespace IMRCRaceControl.Models;

/// <summary>
/// A crossing that was observed below the configured minimum lap time.
/// It is retained for breakout/audit purposes but does not increment the
/// racer's scored lap count.
/// </summary>
public sealed record BreakoutRecord(
    long Sequence,
    int CarNumber,
    string DriverName,
    int AttemptedLapNumber,
    TimeSpan LapTime,
    TimeSpan Elapsed,
    TimeSpan MinimumLapTime,
    DateTimeOffset Timestamp,
    string Source)
{
    public string LapTimeDisplay => RaceEngine.FormatLap(LapTime);
    public string ElapsedDisplay => RaceEngine.FormatElapsed(Elapsed);
    public string MinimumLapDisplay => RaceEngine.FormatLap(MinimumLapTime);
}
