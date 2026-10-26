namespace IMRCRaceControl.Models;
public sealed record ClassPrintColor(string Name, string Hex);
public static class ClassPrintColors
{
    public static readonly ClassPrintColor[] Choices = [new("Blue", "#BCD8F4"), new("Green", "#BDF1BB"), new("Yellow", "#FFF7A8"), new("Peach", "#FFD2AD"), new("Pink", "#F6C5D9"), new("Lavender", "#D9CEF5"), new("Aqua", "#BAEBE8"), new("Gray", "#DCE1E6")];
    public static string Safe(string? hex) => Choices.FirstOrDefault(c => c.Hex.Equals(hex, StringComparison.OrdinalIgnoreCase))?.Hex ?? Choices[0].Hex;
}
