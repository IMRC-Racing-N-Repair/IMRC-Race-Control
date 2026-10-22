namespace IMRCRaceControl.Models;

public enum ToneLength
{
    Short,
    Normal,
    Long
}

public static class ToneLengthExtensions
{
    public static string ToDisplay(this ToneLength length) => length switch
    {
        ToneLength.Short => "Short audible",
        ToneLength.Long => "Long audible",
        _ => "Normal audible"
    };
}
