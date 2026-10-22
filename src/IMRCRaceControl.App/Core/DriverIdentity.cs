using System.Security.Cryptography;
using System.Text;

namespace IMRCRaceControl.Core;

public static class DriverIdentity
{
    // Existing GUIDs are already the driver's UUID. Stable fallback for old
    // imported string IDs avoids changing any database or result references.
    public static string UuidFor(string id)
    {
        if (Guid.TryParse(id, out var guid)) return guid.ToString("D");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("IMRC-driver:" + id))[..16];
        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x80); // custom UUID v8, Guid byte order
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes).ToString("D");
    }
}
