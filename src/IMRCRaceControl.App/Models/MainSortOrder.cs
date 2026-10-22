namespace IMRCRaceControl.Models;

public enum MainSortOrder
{
    QualifyingRank,
    DriverSeed,
    RegistrationOrder,
    Random
}

public static class MainSortOrderExtensions
{
    public static string ToDisplay(this MainSortOrder order) => order switch
    {
        MainSortOrder.QualifyingRank => "Qualifying Rank",
        MainSortOrder.DriverSeed => "Driver / Class Seed",
        MainSortOrder.RegistrationOrder => "Event Registration Order",
        MainSortOrder.Random => "Random",
        _ => order.ToString()
    };
}
