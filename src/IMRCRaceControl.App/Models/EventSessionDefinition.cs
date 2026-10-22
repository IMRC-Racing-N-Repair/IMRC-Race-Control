namespace IMRCRaceControl.Models;

public sealed class EventSessionDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string ClassId { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public SessionType SessionType { get; set; } = SessionType.Qualifying;
    public StartStyle StartStyle { get; set; } = StartStyle.IndividualCrossing;
    public RaceFormat RaceFormat { get; set; } = global::IMRCRaceControl.Models.RaceFormat.Timed;
    public double? RaceMinutesOverride { get; set; }
    public int TargetLaps { get; set; } = 50;
    public QualificationOrder QualificationOrder { get; set; } = QualificationOrder.Random;
    public int Round { get; set; } = 1;
    public int Heat { get; set; } = 1;
    public int QueueOrder { get; set; }
    // Racer-facing number, repeated for the same class/heat in later rounds.
    // QueueOrder remains the separate, unique event-wide running order.
    public int RaceNumber { get; set; }

    // Main-only structure. A=Heat/MainOrder 1, B=2, etc.
    // MainLeg identifies repeated A/B/C main runs (for example A1/A2/A3).
    public int MainCapacity { get; set; }
    public int MainLeg { get; set; } = 1;
    public int MainRunCount { get; set; } = 1;
    public int MainCountBest { get; set; } = 1;
    public MainSortOrder MainSortOrder { get; set; } = global::IMRCRaceControl.Models.MainSortOrder.QualifyingRank;
    public int BumpInCount { get; set; }
    public int BumpUpCount { get; set; }
    public string BumpTargetSessionId { get; set; } = string.Empty;

    public EventSessionStatus ProgressStatus { get; set; } = EventSessionStatus.Planned;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<SessionEntrant> Entrants { get; set; } = [];
    public List<StandingRow> Results { get; set; } = [];
    public List<LapRecord> Laps { get; set; } = [];
    public List<BreakoutRecord> Breakouts { get; set; } = [];

    public string TypeDisplay => SessionType.ToString();
    public string StartStyleDisplay => StartStyle.ToDisplay();
    public string QualificationOrderDisplay => QualificationOrder.ToDisplay();
    public string StatusDisplay => ProgressStatus.ToDisplay();

    public string HeatMainDisplay => SessionType == SessionType.Race
        ? (Heat is >= 1 and <= 26 ? ((char)('A' + Heat - 1)).ToString() : Heat.ToString())
        : Heat.ToString();

    public string MainRunDisplay => SessionType == SessionType.Race && MainRunCount > 1
        ? $"{MainLeg}/{MainRunCount}"
        : "—";

    [System.Text.Json.Serialization.JsonIgnore]
    public int ScheduleStageOrder => SessionType == SessionType.Qualifying ? 0 : SessionType == SessionType.Race ? 1 : 2;
    [System.Text.Json.Serialization.JsonIgnore]
    public int ScheduleRoundOrder => SessionType == SessionType.Race ? MainLeg : Round;

    [System.Text.Json.Serialization.JsonIgnore]
    public string RoundGroupHeading => SessionType == SessionType.Qualifying ? $"QUALIFYING ROUND {Round}"
        : SessionType == SessionType.Race ? $"MAIN ROUND {MainLeg}" : $"PRACTICE ROUND {Round}";
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsAlternateRoundBand => ScheduleRoundOrder % 2 == 0;

    public string RoundLabel => SessionType == SessionType.Qualifying ? $"Q{Round}"
        : SessionType == SessionType.Race ? $"M{MainLeg}" : $"P{Round}";

    public string DashboardDisplay =>
        $"#{RaceNumber} • {ClassName} • " +
        (SessionType == SessionType.Race ? $"{HeatMainDisplay}-Main" : $"Heat {Heat}");

    public int RacerCount => Entrants.Count;
}
