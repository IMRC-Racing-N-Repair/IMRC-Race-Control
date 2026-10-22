namespace IMRCRaceControl.Models;

public enum QualifyingPointsMode { OverallEvent, EveryRound, BestRound }
public enum SeriesTieBreak { WinsThenBestFinish, BestWeek, MostEvents }

public sealed class StagePointsSettings
{
    public bool Enabled { get; set; }
    public string PositionPoints { get; set; } = "10,9,8,7,6,5,4,3,2,1";
    public decimal UnlistedPositionPoints { get; set; }
    public decimal PointsPerLap { get; set; }
    public decimal ParticipationPoints { get; set; }
    public decimal WinBonus { get; set; }
    public decimal FastLapBonus { get; set; }
    public int MinimumLaps { get; set; } = 1;
}

public sealed class SeriesPointsSettings
{
    public StagePointsSettings Qualifying { get; set; } = new();
    public StagePointsSettings Mains { get; set; } = new() { Enabled = true, PositionPoints = RaceSeriesDefinition.DefaultPointsCsv };
    public QualifyingPointsMode QualifyingMode { get; set; }
    public decimal TqBonus { get; set; }
    public decimal EventAttendancePoints { get; set; }
    public SeriesTieBreak TieBreak { get; set; }
    public bool LegacyQualifyingFallback { get; set; }
}

public sealed class SeriesPointsAdjustment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DriverId { get; set; } = "";
    public string DriverName { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string EventId { get; set; } = ""; // Empty = championship adjustment, never dropped.
    public string EventName { get; set; } = "Series total";
    public decimal Amount { get; set; }
    public string Note { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class SeriesWeekScore
{
    public string EventId { get; set; } = "";
    public string EventName { get; set; } = "";
    public DateTime EventDate { get; set; }
    public bool Attended { get; set; }
    public bool Pending { get; set; }
    public bool Counted { get; set; }
    public int Finish { get; set; }
    public decimal Qualifying { get; set; }
    public decimal Mains { get; set; }
    public decimal Bonuses { get; set; }
    public decimal Adjustment { get; set; }
    public string Notes { get; set; } = "";
    public decimal Total => Qualifying + Mains + Bonuses + Adjustment;
    public string Status => Pending ? "PENDING" : !Attended && Adjustment == 0 ? "ABSENT" : Counted ? "COUNTED" : "DROPPED";
    public string Display => Pending ? "Pending" : !Attended && Adjustment == 0 ? "—" : Counted ? Total.ToString("0.##") : $"({Total:0.##}) dropped";
}
