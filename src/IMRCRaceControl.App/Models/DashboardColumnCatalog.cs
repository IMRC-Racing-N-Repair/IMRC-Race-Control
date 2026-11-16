namespace IMRCRaceControl.Models;

public static class DashboardColumnCatalog
{
    public static readonly IReadOnlyList<string> LockedColumns = new[]
    {
        "POS", "#", "DRIVER"
    };

    // Display order: PACE follows LAPS in the default live dashboard.
    // STATUS and ACTION/MANUAL are no longer dashboard columns.
    public static readonly IReadOnlyList<string> OptionalColumns = new[]
    {
        "LAPS",
        "PACE",
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
        "TOTAL"
    };

    public static readonly IReadOnlyList<string> DefaultVisibleColumns = OptionalColumns
        .Where(c => c is not ("TOP 5" or "AVG 5" or "AVG 10" or "AVG 15" or "CONSIST" or "TOTAL"))
        .ToArray();

    // Original default before 0.17.11; only auto-migrate that full default,
    // preserving an operator's deliberately customized visibility/order.
    public static readonly IReadOnlyList<string> LegacyDefaultOrder = new[]
    {
        "LAPS", "LAST LAP", "BEST LAP", "CLOCK", "GAP LEADER",
        "GAP BEHIND", "TOP 2", "TOP 3", "TOP 5", "AVG",
        "AVG 5", "AVG 10", "AVG 15", "CONSIST", "PACE",
        "TOTAL", "STATUS", "MANUAL"
    };

    public static IReadOnlyList<string> AllColumns => LockedColumns.Concat(OptionalColumns).ToList();

    public static bool IsLegacyDefault(IReadOnlyList<string>? columns) =>
        columns is not null && columns.SequenceEqual(LegacyDefaultOrder, StringComparer.OrdinalIgnoreCase);

    public static bool IsLegacyDefaultVisibility(IReadOnlyList<string>? columns) =>
        columns is not null &&
        columns.Count == LegacyDefaultOrder.Count &&
        LegacyDefaultOrder.All(c => columns.Contains(c, StringComparer.OrdinalIgnoreCase));
}
