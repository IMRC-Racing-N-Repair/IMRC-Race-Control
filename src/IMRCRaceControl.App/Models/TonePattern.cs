namespace IMRCRaceControl.Models;

public enum TonePattern
{
    StartChime,
    WarningTriple,
    FinishDrop,
    PassingTick,
    HighPing,
    DoublePulse,
    LongBeep,
    DriverFinish,
    Halfway,
    AirHorn,
    Silent
}

public static class TonePatternExtensions
{
    public static string ToDisplay(this TonePattern tone) => tone switch
    {
        TonePattern.StartChime => "Start chime",
        TonePattern.WarningTriple => "Warning triple",
        TonePattern.FinishDrop => "Finish drop",
        TonePattern.PassingTick => "Passing tick",
        TonePattern.HighPing => "High ping",
        TonePattern.DoublePulse => "Double pulse",
        TonePattern.LongBeep => "Long start beep",
        TonePattern.DriverFinish => "Driver finish (descending pair)",
        TonePattern.Halfway => "Halfway (low double)",
        TonePattern.AirHorn => "Air horn",
        TonePattern.Silent => "Silent / off",
        _ => tone.ToString()
    };
}
