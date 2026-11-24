using Velopack;
namespace IMRCRaceControl;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Explicit previews use an isolated WPF lifetime. Do not acquire the
        // operator mutex, invoke updater hooks, or initialize the race database.
        if (args.Length >= 2 && (args[0] == "--export-brand" || args[0] == "--export-rentals"))
        {
            var output = args[1];
            System.IO.Directory.CreateDirectory(output);
            var phase = args.Length == 3 ? args[2] : "all";
            var preview = new App
            {
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
                PreviewErrorFile = System.IO.Path.Combine(output, args[0] == "--export-brand"
                    ? "Brand-UI-Error.txt" : "Transponder-UI-Error.txt"),
                PreviewAction = args[0] == "--export-brand"
                    ? () => Services.BrandAssetExporter.Export(output)
                    : () => Controls.RentalTransponderView.ExportPreview(output, phase)
            };
            preview.InitializeComponent();
            return preview.Run();
        }
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        using var instance = new Mutex(true,@"Local\IMRC.RaceControl."+Environment.UserName,out var firstInstance);
        if (!firstInstance)
        {
            System.Windows.MessageBox.Show("IMRC Race Control is already running. Close that copy before opening or repairing the app.","IMRC Race Control");
            return 0;
        }
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
