namespace IMRCRaceControl.Models;

public enum EventStatus
{
    Active = 0,
    Completed = 1
}

public static class EventStatusExtensions
{
    public static string ToDisplay(this EventStatus status) => status switch
    {
        EventStatus.Completed => "COMPLETED",
        _ => "ACTIVE"
    };
}
