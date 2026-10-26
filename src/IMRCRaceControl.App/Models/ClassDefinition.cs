namespace IMRCRaceControl.Models;

public sealed class ClassDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Class";
    private string _printColor = ClassPrintColors.Choices[0].Hex;
    public string PrintColor { get => _printColor; set => _printColor = ClassPrintColors.Safe(value); }

    public RaceFormat RaceFormat { get; set; } = global::IMRCRaceControl.Models.RaceFormat.Timed;
    // Null preserves the shared format used by older databases.
    public RaceFormat? MainRaceFormat { get; set; }
    public double? MainRaceMinutes { get; set; }
    public int? MainTargetLaps { get; set; }
    public double MinutesFor(SessionType type) => type == SessionType.Race ? MainRaceMinutes ?? RaceMinutes : RaceMinutes;
    public int LapsFor(SessionType type) => type == SessionType.Race ? MainTargetLaps ?? TargetLaps : TargetLaps;
    public RaceFormat FormatFor(SessionType type) => type == SessionType.Race ? MainRaceFormat ?? RaceFormat : RaceFormat;
    public string MainRaceFormatDisplay => FormatFor(SessionType.Race).ToDisplay();
    public double RaceMinutes { get; set; } = 5;
    public int TargetLaps { get; set; } = 50;
    public double MinimumLapSeconds { get; set; } = 1.0;

    /// <summary>
    /// Target used when the program automatically builds qualifying heats.
    /// Manual heat editing is intentionally allowed to exceed this number.
    /// </summary>
    public int MaxRacersPerHeat { get; set; } = 19;

    /// <summary>
    /// Default capacity used for newly-created main rules.
    /// Individual A/B/C/etc rules may override this capacity.
    /// </summary>
    public int MaxRacersPerMain { get; set; } = 19;

    /// <summary>
    /// A/B/C/etc main structure. A rule's BumpInsFromLower reserves positions
    /// that will be filled automatically by the next lower main's finishers.
    /// </summary>
    public List<MainRuleDefinition> MainRules { get; set; } =
    [
        new MainRuleDefinition
        {
            Order = 1,
            MaxRacers = 19,
            BumpInsFromLower = 0
        }
    ];

    public StartStyle QualStartStyle { get; set; } = StartStyle.IndividualCrossing;
    public StartStyle RaceStartStyle { get; set; } = StartStyle.Tone;

    public string QualStyleDisplay => QualStartStyle.ToDisplay();
    public string RaceStyleDisplay => RaceStartStyle.ToDisplay();
    public string RaceFormatDisplay => RaceFormat.ToDisplay();

    public string RaceLengthDisplay => RaceFormat == global::IMRCRaceControl.Models.RaceFormat.Laps
        ? $"{TargetLaps} laps"
        : $"{RaceMinutes:0.###} min";

    public string MainRuleSummary
    {
        get
        {
            var rules = (MainRules ?? [])
                .OrderBy(r => r.Order)
                .Take(4)
                .Select(r => r.RuleDisplay)
                .ToList();

            return rules.Count == 0
                ? $"{MaxRacersPerMain} racers/main"
                : string.Join(" • ", rules) + ((MainRules?.Count ?? 0) > 4 ? " …" : string.Empty);
        }
    }
}
