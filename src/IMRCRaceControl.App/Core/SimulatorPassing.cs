using System.Globalization;
using System.Text;

namespace IMRCRaceControl.Core;

/// <summary>Emits the same validated enhanced records as the decoder path consumes.</summary>
public static class SimulatorPassing
{
    public static byte[] Encode(uint transponder, uint sequence, DateTimeOffset time, int hits, int signal)
    {
        var text = string.Create(CultureInfo.InvariantCulture,
            $"@\t100\t{sequence}\t{transponder}\t{time.UtcDateTime.TimeOfDay.TotalSeconds:F3}\t{hits}\t{signal}\t0\t");
        ushort crc = 0xffff;
        foreach (var value in Encoding.ASCII.GetBytes(text))
        {
            for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc << 1) ^ ((crc & 0x8000) != 0 ? 0x1021 : 0));
            crc ^= value;
        }
        return Encoding.ASCII.GetBytes($"\x01{text}x{crc:X4}\r\n");
    }
}
