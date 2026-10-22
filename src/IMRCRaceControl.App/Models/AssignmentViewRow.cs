namespace IMRCRaceControl.Models;

public sealed class AssignmentViewRow
{
    public string AssignmentId { get; init; } = string.Empty;
    public string DriverId { get; init; } = string.Empty;
    public string Driver { get; init; } = string.Empty;
    public string ClassId { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public string TransponderId { get; init; } = string.Empty;
    public string RatingDisplay { get; init; } = "1500";
    public string SofDisplay { get; init; } = "—";
    public string SafetyDisplay { get; init; } = "3.00";
    public int SeedRank { get; init; }
}
