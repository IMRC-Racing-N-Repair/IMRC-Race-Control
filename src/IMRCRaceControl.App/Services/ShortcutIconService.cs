using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using Velopack.Locators;
using Velopack.Windows;

namespace IMRCRaceControl.Services;

internal static class ShortcutIconService
{
    // Only update existing app-owned links. Never recreate a shortcut the user removed.
    public static void RefreshExisting()
    {
        try
        {
            var locator = VelopackLocator.Current;
            if (locator.IsPortable || locator.AppId != "IMRCRaceControl.App") return;
            var root = locator.RootAppDir;
            if (string.IsNullOrWhiteSpace(root)) return;
            var launcher = Path.Combine(root, "IMRCRaceControl.exe");
            if (!File.Exists(launcher)) return;
            using var resource = Application.GetResourceStream(
                new Uri("pack://application:,,,/IMRCRaceControl;component/Assets/RC.ico")).Stream;
            using var buffer = new MemoryStream();
            resource.CopyTo(buffer);
            var bytes = buffer.ToArray();
            // A new artwork path avoids Windows reusing the old exe's cached icon.
            var icon = Path.Combine(root, $"RC-{Convert.ToHexString(SHA256.HashData(bytes))[..16]}.ico");
            if (!File.Exists(icon)) File.WriteAllBytes(icon, bytes);
            var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IMRCRaceControl");
            foreach (var folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Programs })
            {
                var directory = Environment.GetFolderPath(folder);
                if (!Directory.Exists(directory)) continue;
                var options = new EnumerationOptions { RecurseSubdirectories = folder == Environment.SpecialFolder.Programs, IgnoreInaccessible = true };
                foreach (var path in Directory.EnumerateFiles(directory, "IMRC Race Control*.lnk", options))
                {
                    try
                    {
                        using var link = new ShellLink(path);
                        var target = Path.GetFullPath(Environment.ExpandEnvironmentVariables(link.Target));
                        var owned = new[] { root, legacy }.Any(basePath =>
                            target.Equals(Path.Combine(basePath, "IMRCRaceControl.exe"), StringComparison.OrdinalIgnoreCase) ||
                            target.Equals(Path.Combine(basePath, "current", "IMRCRaceControl.exe"), StringComparison.OrdinalIgnoreCase));
                        if (!owned) continue;
                        if (link.IconPath == icon && link.IconIndex == 0 && link.Target == launcher) continue;
                        link.Target = launcher;
                        link.WorkingDirectory = root;
                        link.IconPath = icon;
                        link.IconIndex = 0;
                        link.Save();
                        SHChangeNotify(0x00002000, 0x0005, path, IntPtr.Zero); // UPDATEITEM, PATHW
                    }
                    catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Shortcut icon refresh: {ex.Message}"); }
                }
            }
        }
        // Shortcut permissions or unavailable shell integration must not block racing.
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Shortcut icon refresh: {ex.Message}"); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string path, IntPtr unused);
}
