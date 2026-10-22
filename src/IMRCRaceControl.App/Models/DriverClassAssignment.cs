namespace IMRCRaceControl.Models;

public sealed class DriverClassAssignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DriverId { get; set; } = string.Empty;
    public string ClassId { get; set; } = string.Empty;
    public string TransponderId { get; set; } = string.Empty;
    public int SeedRank { get; set; }
}
