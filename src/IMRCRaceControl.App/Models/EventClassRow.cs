namespace IMRCRaceControl.Models;

public sealed class EventClassRow
{
    public int Order { get; set; }
    public string ClassId { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public int RacersPerHeat { get; set; }
}
