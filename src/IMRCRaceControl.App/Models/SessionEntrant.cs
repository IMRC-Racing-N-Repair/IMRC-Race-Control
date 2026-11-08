namespace IMRCRaceControl.Models;

public sealed class SessionEntrant
{
    public string SourceSessionId { get; set; } = string.Empty;
    public int SourceCarNumber { get; set; }
    public int CarNumber { get; set; }
    public string DriverId { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string TransponderId { get; set; } = string.Empty;
    public int SeedRank { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Nickname) ? DriverName : Nickname;
}
