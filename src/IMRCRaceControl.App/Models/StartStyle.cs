namespace IMRCRaceControl.Models;

public enum StartStyle
{
    Tone,
    LeaderCrossing,
    IndividualCrossing
}

public static class StartStyleExtensions
{
    public static string ToDisplay(this StartStyle style) => style switch
    {
        StartStyle.Tone => "All clocks start at the tone",
        StartStyle.LeaderCrossing => "All clocks start when leader crosses line",
        StartStyle.IndividualCrossing => "Each racer's clock starts when they cross line",
        _ => style.ToString()
    };

    public static StartStyle FromDisplay(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Contains("leader", StringComparison.OrdinalIgnoreCase))
            return StartStyle.LeaderCrossing;
        if (value.Contains("individual", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("each racer", StringComparison.OrdinalIgnoreCase))
            return StartStyle.IndividualCrossing;
        return StartStyle.Tone;
    }
}
