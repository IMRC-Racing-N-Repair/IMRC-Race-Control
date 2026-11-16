using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace IMRCRaceControl;
internal sealed class StartupLoadingWindow : Window
{
    private readonly TextBlock _status;
    public StartupLoadingWindow()
    {
        Width = 440; Height = 295; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty,"PageBrush");
        SetResourceReference(ForegroundProperty,"TextBrush");
        var panel = new StackPanel { Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center };
        var logo = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/IMRCRaceControl;component/Assets/RC-Approved.png")),
            Width = 125, Height = 125, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0,5,0,8)
        };
        panel.Children.Add(logo);
        panel.Children.Add(new TextBlock
        {
            Text = "IMRC RACE CONTROL", FontSize = 24, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        _status = new TextBlock { Text = "Loading Race Control…",
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,14,0,14) };
        panel.Children.Add(_status);
        var progress = new ProgressBar { IsIndeterminate = true, Height = 6, Width = 350 };
        progress.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        panel.Children.Add(progress);
        Content = panel;
    }
    public void SetStatus(string status) => _status.Text = status;
}
