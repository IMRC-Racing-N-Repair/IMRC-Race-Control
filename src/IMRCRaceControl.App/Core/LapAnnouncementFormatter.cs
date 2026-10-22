using System.Globalization;

namespace IMRCRaceControl.Core;

public static class LapAnnouncementFormatter
{
    // Truncate to tenths, allowing a 10 ms grace window just below the next
    // tenth. Work in ticks to avoid binary floating-point boundary errors.
    // This affects speech only; the timing engine retains the exact lap.
    public static string Format(TimeSpan lap, bool tenthsOnly)
    {
        if (lap <= TimeSpan.Zero)
            return string.Empty;

        const long tenth = TimeSpan.TicksPerSecond / 10;
        const long grace = TimeSpan.TicksPerMillisecond * 10;
        var tenths = lap.Ticks / tenth;
        if (lap.Ticks % tenth >= tenth - grace)
            tenths++;

        var digit = (tenths % 10).ToString(CultureInfo.InvariantCulture);
        return tenthsOnly ? digit
            : $"{(tenths / 10).ToString(CultureInfo.InvariantCulture)} {digit}";
    }
}
