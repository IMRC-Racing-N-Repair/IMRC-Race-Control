using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace IMRCRaceControl.Installer;

internal enum InstallAction { Install, Update, Repair, NewerInstalled }
internal sealed record Installation(string Root, Version? Current, Version Target)
{
    public InstallAction Action => Current is null ? InstallAction.Install :
        Current > Target ? InstallAction.NewerInstalled : Current == Target ? InstallAction.Repair : InstallAction.Update;
    public static Version BuildVersion => Normalize(Assembly.GetExecutingAssembly().GetName().Version!);
    private static Version Normalize(Version value) => new(value.Major, value.Minor, Math.Max(0, value.Build));
    public static Installation Detect(string? root = null)
    {
        if (root is null)
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\IMRCRaceControl.App");
            root = key?.GetValue("InstallLocation") as string;
            if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IMRCRaceControl.App");
        }
        root = Path.GetFullPath(root);
        var exe = Path.Combine(root, "current", "IMRCRaceControl.exe");
        Version? current = null;
        if (File.Exists(exe))
        {
            var text = FileVersionInfo.GetVersionInfo(exe).FileVersion;
            if (!Version.TryParse(text, out var found)) throw new IOException("The installed version could not be identified. Open Race Control to check its version before installing.");
            current = Normalize(found);
        }
        return new Installation(root, current, BuildVersion);
    }
}

internal static class InstallerEngine
{
    public static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IMRC Racing N Repair", "Race Control", "InstallerLogs");
    public static string? LastLog { get; private set; }

    public static async Task InstallAsync(Installation installation, Action<string> status)
    {
        var fresh = Installation.Detect(installation.Root);
        if (fresh.Action == InstallAction.NewerInstalled) throw new InvalidOperationException("A newer version is already installed. Open that version instead.");
        // Reserve the app's instance name until installation finishes. Desktop
        // launches cannot start a new race in the middle of file replacement.
        using var appGate = new Mutex(false, @"Local\IMRC.RaceControl." + Environment.UserName, out var canInstall);
        if (!canInstall)
            throw new InvalidOperationException("Close Race Control first, then choose Try again. Your race or practice session will not be interrupted.");
        Directory.CreateDirectory(LogDirectory);
        LastLog = Path.Combine(LogDirectory, $"Install-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        var temp = Path.Combine(Path.GetTempPath(), "IMRC-Race-Control-Setup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            status("Preparing installation files…");
            var payload = Path.Combine(temp, "RaceControl-Setup.exe");
            using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("RaceControl.Setup.exe") ?? throw new IOException("The embedded installer is missing. Download RaceControl-Installer.exe again."))
            await using (var target = new FileStream(payload, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await source.CopyToAsync(target);
            status("Installing Race Control. Please keep this window open…");
            var start = new ProcessStartInfo(payload) { UseShellExecute = false, CreateNoWindow = true };
            // Confirmation is handled by our window. Velopack still owns installation,
            // shortcuts and updates; it never displays its stock confirmation dialog.
            start.ArgumentList.Add("--silent");
            start.ArgumentList.Add("--installto"); start.ArgumentList.Add(installation.Root);
            start.ArgumentList.Add("--log"); start.ArgumentList.Add(LastLog);
            using var process = Process.Start(start) ?? throw new IOException("The installer could not start.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new IOException($"Installation did not complete (code {process.ExitCode}). View the install log for details.");
            if (Installation.Detect(installation.Root).Current != installation.Target)
                throw new IOException("The expected app version was not installed. View the install log for details.");
            status("Installation complete.");
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(LastLog, $"\n{DateTimeOffset.Now:O}\n{ex}\n"); } catch { }
            throw;
        }
        finally
        {
            // Delete only this invocation's temporary payload, never app or data folders.
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    public static void Launch(string root)
    {
        var exe = Path.Combine(root, "IMRCRaceControl.exe");
        if (!File.Exists(exe)) exe = Path.Combine(root, "current", "IMRCRaceControl.exe");
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = root });
    }
}
