namespace IMRCRaceControl.Models;
public sealed record ClassPrintColor(string Name, string Hex);
public static class ClassPrintColors
{
    public static readonly ClassPrintColor[] Choices = [new("Blue", "#009FE3"), new("Green", "#24BD60"), new("Yellow", "#F2D000"), new("Orange", "#F58A1F"), new("Pink", "#ED54A4"), new("Purple", "#B078E8"), new("Aqua", "#00BFAE"), new("Gray", "#999FA6"), new("Red", "#EF5350")];
    private static readonly string[] Legacy = ["#BCD8F4", "#BDF1BB", "#FFF7A8", "#FFD2AD", "#F6C5D9", "#D9CEF5", "#BAEBE8", "#DCE1E6"];
    public static string Safe(string? hex)
    {
        var old = Array.FindIndex(Legacy, c => c.Equals(hex, StringComparison.OrdinalIgnoreCase));
        return old >= 0 ? Choices[old].Hex : Choices.FirstOrDefault(c => c.Hex.Equals(hex, StringComparison.OrdinalIgnoreCase))?.Hex ?? Choices[0].Hex;
    }
}
