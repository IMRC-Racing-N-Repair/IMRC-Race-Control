namespace IMRCRaceControl.Core;

// Trackmate RC / Iridium serial interoperability settings, not the slot-car system.
public static class TrackmateProtocol
{
    public const int BaudRate = 115200;

    public static bool IsProfile(string connectionType) =>
        connectionType.StartsWith("Trackmate", StringComparison.OrdinalIgnoreCase);

    // Old builds saved their forced 9600 preset in the shared serial field.
    public static int ResolveBaudRate(int? savedTrackmateRate, int? legacyRate = null) =>
        savedTrackmateRate is > 0 ? savedTrackmateRate.Value :
        legacyRate is > 0 and not 9600 ? legacyRate.Value : BaudRate;
    public static byte[] StartCommand => [0x01, 0x3f, 0x2c, 0x32, 0x30, 0x32, 0x2c, 0x30, 0x2c, 0x31, 0x31, 0x2c, 0x0d, 0x0a];
}
