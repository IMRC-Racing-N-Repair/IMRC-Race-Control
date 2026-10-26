using Velopack;
namespace IMRCRaceControl;
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        using var instance = new Mutex(true,@"Local\IMRC.RaceControl."+Environment.UserName,out var firstInstance);
        if (!firstInstance)
        {
            System.Windows.MessageBox.Show("IMRC Race Control is already running. Close that copy before opening or repairing the app.","IMRC Race Control");
            return;
        }
        var app = new App();
        app.InitializeComponent();
        if (args.Length == 2 && args[0] == "--export-brand")
        {
            Services.BrandAssetExporter.Export(args[1]);
            return;
        }
        if (args.Length == 2 && args[0] == "--export-rentals")
        {
            // Closing a preview window must not shut down WPF before the next theme.
            app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            try { Controls.RentalTransponderView.ExportPreview(args[1]); }
            catch (Exception ex)
            {
                System.IO.Directory.CreateDirectory(args[1]);
                System.IO.File.WriteAllText(System.IO.Path.Combine(args[1], "Transponder-UI-Error.txt"), ex.ToString());
                Environment.ExitCode = 1;
            }
            return;
        }
        app.Run();
    }
}
