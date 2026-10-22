namespace IMRCRaceControl.Models;

public static class DashboardColumnCatalog
{
    public static readonly IReadOnlyList<string> LockedColumns = new[]
    {
        "POS", "#", "DRIVER"
    };

    public static readonly IReadOnlyList<string> OptionalColumns = new[]
    {
        "LAPS",
        "LAST LAP",
        "BEST LAP",
        "CLOCK",
        "GAP LEADER",
        "GAP BEHIND",
        "TOP 2",
        "TOP 3",
        "TOP 5",
        "AVG",
        "AVG 5",
        "AVG 10",
        "AVG 15",
        "CONSIST",
        "PACE",
        "TOTAL",
        "STATUS",
        "MANUAL"
    };

    public static IReadOnlyList<string> AllColumns => LockedColumns.Concat(OptionalColumns).ToList();
}
