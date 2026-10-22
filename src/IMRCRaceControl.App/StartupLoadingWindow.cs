using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace IMRCRaceControl;
internal sealed class StartupLoadingWindow : Window
{
    private readonly TextBlock _status;
    public StartupLoadingWindow()
    {
        Width = 480; Height = 245; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(16,18,20)); Foreground = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new Controls.BrandLogo { Width = 350, Height = 135, HorizontalAlignment = HorizontalAlignment.Center });
        _status = new TextBlock { Text = "Starting…", Margin = new Thickness(0,16,0,12) };
        panel.Children.Add(_status);
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 5, Foreground = new SolidColorBrush(Color.FromRgb(227,75,75)) });
        Content = panel;
    }
    public void SetStatus(string status) => _status.Text = status;
}
