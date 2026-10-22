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
        app.Run();
    }
}
