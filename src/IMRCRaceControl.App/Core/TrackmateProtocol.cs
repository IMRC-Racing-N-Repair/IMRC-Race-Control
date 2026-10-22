namespace IMRCRaceControl.Core;

// Trackmate RC / Iridium serial interoperability settings, not the slot-car system.
public static class TrackmateProtocol
{
    public const int BaudRate = 9600;
    public static byte[] StartCommand => [0x01, 0x3f, 0x2c, 0x32, 0x30, 0x32, 0x2c, 0x30, 0x2c, 0x31, 0x31, 0x2c, 0x0d, 0x0a];
}
