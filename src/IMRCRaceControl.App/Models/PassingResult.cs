namespace IMRCRaceControl.Models;

public sealed record PassingResult(
    bool Accepted,
    string Message,
    LapRecord? Lap = null,
    PassingResultCode Code = PassingResultCode.None,
    bool IsNewPersonalBest = false,
    bool IsSessionBest = false,
    TimeSpan? BestLap = null,
    int? BestLapNumber = null,
    BreakoutRecord? Breakout = null);
