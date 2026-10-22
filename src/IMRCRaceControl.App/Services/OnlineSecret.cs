using System.Security.Cryptography;
using System.Text;
namespace IMRCRaceControl.Services;
public static class OnlineSecret
{
    public static string Protect(string key) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
    public static string Unprotect(string value) => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
}
