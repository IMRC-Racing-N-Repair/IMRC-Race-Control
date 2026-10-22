namespace IMRCRaceControl.Models;

public sealed class EventRosterRow
{
    public string AssignmentId { get; set; } = string.Empty;
    public string DriverId { get; set; } = string.Empty;
    public string ClassId { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string TransponderId { get; set; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Nickname) ? DriverName : Nickname;
    public string TransponderDisplay => string.IsNullOrWhiteSpace(TransponderId) ? "Not set" : TransponderId;
}
