using System.Globalization;

namespace IMRCRaceControl.Core;

public static class LapAnnouncementFormatter
{
    // Truncate toward zero to tenths. Never round up near a tenth boundary:
    // a 4.399 lap is announced as 4.3, not 4.4.
    // This affects speech only; the timing engine retains the exact lap.
    public static string Format(TimeSpan lap, bool tenthsOnly)
    {
        if (lap <= TimeSpan.Zero)
            return string.Empty;

        const long tenth = TimeSpan.TicksPerSecond / 10;
        var tenths = lap.Ticks / tenth;

        var digit = (tenths % 10).ToString(CultureInfo.InvariantCulture);
        return tenthsOnly ? digit
            : $"{(tenths / 10).ToString(CultureInfo.InvariantCulture)} {digit}";
    }
}
