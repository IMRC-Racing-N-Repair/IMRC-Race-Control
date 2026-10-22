namespace IMRCRaceControl.Models;

public sealed class DashboardColumnOption
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public bool IsVisible { get; set; } = true;
}
