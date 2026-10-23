using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace IMRCRaceControl.Installer;
public partial class InstallerWindow : Window
{
    private readonly Installation _installation;
    private readonly bool _preview;
    private bool _busy, _finished, _failed;
    internal InstallerWindow(Installation installation, bool preview = false)
    {
        _installation = installation; _preview = preview;
        InitializeComponent();
        CurrentVersion.Text = installation.Current?.ToString(3) ?? "Not installed";
        TargetVersion.Text = installation.Target.ToString(3);
        InstallLocation.Text = installation.Root;
        FolderButton.IsEnabled = Directory.Exists(installation.Root) || preview;
        (Heading.Text, Description.Text, ActionButton.Content) = installation.Action switch
        {
            InstallAction.Install => ("Ready for race day", "Install Race Control on this computer. Everything you need is included in this installer.", "Install Race Control"),
            InstallAction.Update => ("A new version is ready", "Race Control is already installed. Update your application and keep your saved races, drivers and settings.", "Update Race Control"),
            InstallAction.Repair => ("Race Control is installed", "This version is already installed. Repair the application files while keeping your saved race data.", "Repair Race Control"),
            _ => ("A newer version is installed", "This installer is older than your installed version. Use the newer version of Race Control already on this computer.", "Newer version installed")
        };
        ActionButton.IsEnabled = installation.Action != InstallAction.NewerInstalled;
    }
    private async void ActionClicked(object sender, RoutedEventArgs e)
    {
        if (_preview || _busy) return;
        if (_finished)
        {
            try { InstallerEngine.Launch(_installation.Root); Close(); }
            catch (Exception ex) { Status.Text = "Race Control could not be opened: " + ex.Message; }
            return;
        }
        SetBusy(true);
        try
        {
            await InstallerEngine.InstallAsync(_installation, text => Dispatcher.Invoke(() => Status.Text = text));
            _finished = true;
            Heading.Text = "Race Control is ready";
            Description.Text = "Installation is complete. Open Race Control when you are ready.";
            CurrentVersion.Text = _installation.Target.ToString(3);
            Status.Text = "Your saved race data has been kept in place.";
            ActionButton.Content = "Launch Race Control";
            CancelButton.Content = "Close";
            FolderButton.Content = "Open install folder";
            _failed = false;
        }
        catch (Exception ex) { ShowFailure(ex.Message); }
        finally { SetBusy(false); }
    }
    internal void ShowFailure(string message)
    {
        _failed = true;
        Heading.Text = "Installation needs attention";
        Description.Text = "Review the message below, then try again when you are ready.";
        Status.Text = message;
        ActionButton.Content = "Try again";
        if (InstallerEngine.LastLog is not null) FolderButton.Content = "View install log";
    }
    internal void BlockInstallation(string message)
    {
        ShowFailure(message); ActionButton.IsEnabled = false;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Activity.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ActionButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        CloseButton.IsEnabled = !busy;
        FolderButton.IsEnabled = !busy && (Directory.Exists(_installation.Root) || (_failed && InstallerEngine.LastLog is not null));
        if (busy) { Heading.Text = "Setting up Race Control"; Description.Text = "Installing or updating your application. Please wait…"; }
    }
    private void FolderClicked(object sender, RoutedEventArgs e)
    {
        if (_preview) return;
        try
        {
            var path = _failed && InstallerEngine.LastLog is not null ? InstallerEngine.LastLog : _installation.Root;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { Status.Text = "Could not open this location: " + ex.Message; }
    }
    private void CloseClicked(object sender, RoutedEventArgs e) { if (!_busy) Close(); }
    private void OnClosing(object? sender, CancelEventArgs e) { if (_busy) e.Cancel = true; }
    private void OnKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape && !_busy) Close(); }
    private void DragHeader(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }

    internal static void ExportPreviews(string output)
    {
        Directory.CreateDirectory(output);
        var target = Installation.BuildVersion;
        var older = new Version(target.Major, target.Minor, Math.Max(0, target.Build - 1));
        foreach (var (name, current) in new (string, Version?)[] { ("Install", null), ("Update", older), ("Repair", target), ("Newer", new Version(target.Major, target.Minor, target.Build + 1)) })
        {
            var window = new InstallerWindow(new Installation(@"C:\Users\Racer\AppData\Local\IMRCRaceControl.App", current, target), true);
            if (name == "Newer" && window.ActionButton.IsEnabled) throw new InvalidOperationException("Downgrades must be blocked.");
            if (name != "Newer" && !window.ActionButton.Content.ToString()!.StartsWith(name, StringComparison.Ordinal)) throw new InvalidOperationException("Installer action does not match installation state.");
            window.Show(); window.UpdateLayout();
            var content = (FrameworkElement)window.Content;
            var image = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32);
            image.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(output, $"Installer-Confirm-{name}.png")); encoder.Save(file);
            window.Close();
        }
    }
}
