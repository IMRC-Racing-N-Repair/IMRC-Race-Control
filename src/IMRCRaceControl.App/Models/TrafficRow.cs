namespace IMRCRaceControl.Models;

public sealed class TrafficRow
{
    public required string Time { get; init; }
    public required string Transport { get; init; }
    public string DecodeSummary { get; init; } = "";
    public int Bytes { get; init; }
    public required string Hex { get; init; }
    public required string Ascii { get; init; }
}
