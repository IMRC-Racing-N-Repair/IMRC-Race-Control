namespace IMRCRaceControl.Models;

public enum RaceFormat
{
    Timed,
    Laps
}

public static class RaceFormatExtensions
{
    public static string ToDisplay(this RaceFormat format) => format switch
    {
        RaceFormat.Timed => "Timed race",
        RaceFormat.Laps => "Lapped race",
        _ => format.ToString()
    };
}
