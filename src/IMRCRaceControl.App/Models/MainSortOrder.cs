namespace IMRCRaceControl.Models;

public enum MainSortOrder
{
    QualifyingRank,
    DriverSeed,
    RegistrationOrder,
    Random,
    OverallRoundResults
}

public static class MainSortOrderExtensions
{
    public static string ToDisplay(this MainSortOrder order) => order switch
    {
        MainSortOrder.QualifyingRank => "Qualifying Rank (round placements)",
        MainSortOrder.OverallRoundResults => "OVERALL ROUND RESULTS — best run across all rounds",
        MainSortOrder.DriverSeed => "Driver / Class Seed",
        MainSortOrder.RegistrationOrder => "Event Registration Order",
        MainSortOrder.Random => "Random",
        _ => order.ToString()
    };
}
