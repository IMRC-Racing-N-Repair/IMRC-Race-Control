namespace IMRCRaceControl.Core;

// Trackmate RC / Iridium serial interoperability settings, not the slot-car system.
public static class TrackmateProtocol
{
    public const int BaudRate = 9600;

    public static bool IsProfile(string connectionType) =>
        connectionType.StartsWith("Trackmate", StringComparison.OrdinalIgnoreCase);

    // Trackmate's existing RC interoperability reference uses 9600 baud.
    // Keep an explicit operator override, but never infer a Trackmate rate from
    // the shared serial setting used by unrelated USB/serial decoder profiles.
    public static int ResolveBaudRate(int? savedTrackmateRate, int? legacyRate = null) =>
        savedTrackmateRate is > 0 ? savedTrackmateRate.Value : BaudRate;
    public static byte[] StartCommand => [0x01, 0x3f, 0x2c, 0x32, 0x30, 0x32, 0x2c, 0x30, 0x2c, 0x31, 0x31, 0x2c, 0x0d, 0x0a];
}
