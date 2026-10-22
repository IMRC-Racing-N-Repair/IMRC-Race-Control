namespace IMRCRaceControl.Models;

public sealed class DriverRatingSettings
{
    public double IncidentSlowdownPercent { get; set; } = 25;
    public double IncidentMinimumSeconds { get; set; } = 1;
    public int MinimumSafetyLaps { get; set; } = 10;
    public double AccidentPenalty { get; set; } = .25;
    public int SafetyWindowLaps { get; set; } = 200;
}

public sealed class DriverRatingReview
{
    public string EventId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string DriverId { get; set; } = "";
    public bool ExcludeSafety { get; set; }
    public string Note { get; set; } = "";
}

public sealed class DriverRatingRow
{
    public string DriverId { get; init; } = "";
    public string Uuid { get; init; } = "";
    public string DriverName { get; init; } = "";
    public string ClassId { get; init; } = "";
    public string ClassName { get; init; } = "";
    public int Rank { get; set; }
    public double Rating { get; set; } = 1500;
    public double Safety { get; set; } = 3;
    public int Races { get; set; }
    public int SafetyLaps { get; set; }
    public int EstimatedIncidents { get; set; }
    public double LastStrengthOfField { get; set; }
    public string RatingDisplay => Rating.ToString("0");
    public string SafetyDisplay => Safety.ToString("0.00");
    public string Tier => Races < 5 ? "Provisional" : Rating >= 2200 ? "Elite" : Rating >= 1900 ? "Expert" : Rating >= 1600 ? "Advanced" : Rating >= 1300 ? "Sport" : "Club";
    public string SafetyTier => SafetyLaps < 50 ? "Provisional" : Safety >= 4 ? "Very clean" : Safety >= 3 ? "Clean" : Safety >= 2 ? "Steady" : "Developing";
}

public sealed class DriverRatingRaceRow
{
    public string EventId { get; init; } = "";
    public string SessionId { get; init; } = "";
    public string DriverId { get; init; } = "";
    public string ClassId { get; init; } = "";
    public DateTimeOffset Date { get; init; }
    public string EventName { get; init; } = "";
    public string SessionName { get; init; } = "";
    public string DriverName { get; init; } = "";
    public int Position { get; init; }
    public double StrengthOfField { get; init; }
    public double RatingBefore { get; init; }
    public double RatingAfter { get; init; }
    public double SafetyBefore { get; init; }
    public double SafetyAfter { get; init; }
    public string SafetyDeltaDisplay => (SafetyAfter - SafetyBefore).ToString("+0.00;-0.00;0.00");
    public double Delta => RatingAfter - RatingBefore;
    public string DeltaDisplay => Delta.ToString("+0.0;-0.0;0.0");
    public int SafetyLaps { get; init; }
    public int EstimatedIncidents { get; init; }
    public double? Consistency { get; init; }
    public string ConsistencyDisplay => Consistency.HasValue ? $"{Consistency:0.0}%" : "—";
    public string SafetyStatus { get; init; } = "";
    public string Note { get; init; } = "";
    public string DateDisplay => Date.LocalDateTime.ToString("MM/dd/yyyy HH:mm");
}
