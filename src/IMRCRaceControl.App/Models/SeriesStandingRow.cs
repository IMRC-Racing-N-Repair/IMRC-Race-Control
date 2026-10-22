namespace IMRCRaceControl.Models;

public sealed class SeriesStandingRow
{
    public int Position { get; set; }
    public string DriverId { get; init; } = string.Empty;
    public string DriverName { get; init; } = string.Empty;
    public int Events { get; set; }
    public int Wins { get; set; }
    public int BestFinish { get; set; } = int.MaxValue;
    public decimal Points { get; set; }
    public int CountedEvents { get; set; }
    public decimal GrossPoints { get; set; }
    public decimal DroppedPoints { get; set; }
    public decimal ManualPoints { get; set; }
    public List<SeriesWeekScore> Weeks { get; set; } = [];
    public Dictionary<string, string> WeekDisplay => Weeks.ToDictionary(w => w.EventId, w => w.Display);
    public string DroppedWeeks => string.Join(", ", Weeks.Where(w => w.Status == "DROPPED").Select(w => $"{w.EventDate:MM/dd} {w.EventName}: {w.Total:0.##}"));

    public string BestFinishDisplay => BestFinish == int.MaxValue ? "—" : BestFinish.ToString();
}
