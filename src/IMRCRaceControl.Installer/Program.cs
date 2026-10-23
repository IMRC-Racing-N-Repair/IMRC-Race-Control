using System.IO;
using System.Windows;
namespace IMRCRaceControl.Installer;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (args.Length == 2 && args[0] == "--export-ui")
        {
            InstallerWindow.ExportPreviews(args[1]); return 0;
        }
        string? root = null;
        for (var i = 0; i < args.Length; i++)
            if (args[i] == "--installto" && i + 1 < args.Length) root = args[++i];
        var silent = args.Contains("--silent") || args.Contains("-s");
        using var mutex = new Mutex(true, @"Local\IMRC.RaceControl.Installer." + Environment.UserName, out var first);
        try
        {
            var installation = Installation.Detect(root);
            if (silent)
            {
                if (!first) return 4;
                InstallerEngine.InstallAsync(installation, _ => { }).GetAwaiter().GetResult();
                return 0;
            }
            var window = new InstallerWindow(installation);
            if (!first)
            {
                window.BlockInstallation("Another Race Control installer is already open. Close this window and use that installer.");
            }
            app.MainWindow = window;
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show(); app.Run(); return 0;
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                var fallback = new Installation(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IMRCRaceControl.App"), null, Installation.BuildVersion);
                var window = new InstallerWindow(fallback);
                window.ShowFailure(ex.Message);
                app.MainWindow = window; app.ShutdownMode = ShutdownMode.OnMainWindowClose; window.Show(); app.Run();
            }
            return 1;
        }
    }
}
