using IMRCRaceControl.Core;

namespace IMRCRaceControl.Models;

public sealed record LapRecord(
    long Sequence,
    int CarNumber,
    string DriverName,
    int LapNumber,
    TimeSpan LapTime,
    TimeSpan Elapsed,
    DateTimeOffset Timestamp,
    string Source,
    bool Finished)
{
    public string LapTimeDisplay => RaceEngine.FormatLap(LapTime);
    public string ElapsedDisplay => RaceEngine.FormatElapsed(Elapsed);
}
