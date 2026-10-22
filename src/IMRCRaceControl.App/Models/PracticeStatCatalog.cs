namespace IMRCRaceControl.Models;

public static class PracticeStatCatalog
{
    public const string Top2Consecutive = "Top 2 Consecutive";
    public const string Top3Consecutive = "Top 3 Consecutive";
    public const string Top5Consecutive = "Top 5 Consecutive";
    public const string AvgTop5 = "Avg Top 5";
    public const string Recent5 = "Recent 5 Avg";
    public const string Consistency = "Consistency";
    public const string LastLap = "Last Lap";
    public const string BestLap = "Best Lap";
    public const string AverageLap = "Average Lap";
    public const string LapCount = "Lap Count";
    public const string StintTime = "Stint Time";

    public static IReadOnlyList<string> All { get; } =
    [
        Top2Consecutive,
        Top3Consecutive,
        Top5Consecutive,
        AvgTop5,
        Recent5,
        Consistency,
        LastLap,
        BestLap,
        AverageLap,
        LapCount,
        StintTime
    ];

    public static IReadOnlyList<string> Defaults { get; } =
    [
        Top2Consecutive,
        AvgTop5,
        Consistency
    ];
}
