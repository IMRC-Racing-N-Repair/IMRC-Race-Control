namespace IMRCRaceControl.Models;

public enum QualificationOrder
{
    Random,
    Ranked,
    PreviousRound,
    Manual
}

public static class QualificationOrderExtensions
{
    public static string ToDisplay(this QualificationOrder order) => order switch
    {
        QualificationOrder.Random => "Random",
        QualificationOrder.Ranked => "Ranked",
        QualificationOrder.PreviousRound => "Previous Round",
        QualificationOrder.Manual => "Manual / Driver List",
        _ => order.ToString()
    };

    public static QualificationOrder FromDisplay(string value)
    {
        if (string.Equals(value, "Previous Round", StringComparison.OrdinalIgnoreCase))
            return QualificationOrder.PreviousRound;
        if (string.Equals(value, "Manual / Driver List", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Manual", StringComparison.OrdinalIgnoreCase))
            return QualificationOrder.Manual;

        return Enum.TryParse<QualificationOrder>(value.Replace(" ", string.Empty), true, out var parsed)
            ? parsed
            : QualificationOrder.Random;
    }
}
