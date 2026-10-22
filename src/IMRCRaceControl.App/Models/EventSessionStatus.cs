namespace IMRCRaceControl.Models;

public enum EventSessionStatus
{
    Planned = 0,
    Loaded = 1,
    Running = 2,
    Completed = 3,
    Skipped = 4
}

public static class EventSessionStatusExtensions
{
    public static string ToDisplay(this EventSessionStatus status) => status switch
    {
        EventSessionStatus.Planned => "PLANNED",
        EventSessionStatus.Loaded => "LOADED",
        EventSessionStatus.Running => "RUNNING",
        EventSessionStatus.Completed => "COMPLETE",
        EventSessionStatus.Skipped => "SKIPPED",
        _ => status.ToString().ToUpperInvariant()
    };
}
