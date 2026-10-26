using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace IMRCRaceControl.Dialogs;
internal static class PrintBranding
{
    public static FrameworkElement Logo()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,0,0,8) };
        panel.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/IMRCRaceControl;component/Assets/RC-Approved.png")), Width = 52, Height = 52 });
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7,0,0,0) };
        name.Children.Add(new TextBlock { Style = null, Text = "IMRC", FontSize = 20, FontWeight = FontWeights.Black, Foreground = Brushes.Black });
        name.Children.Add(new TextBlock { Style = null, Text = "RACE CONTROL", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        panel.Children.Add(name); return panel;
    }
    public static Brush Color(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(IMRCRaceControl.Models.ClassPrintColors.Safe(hex)));
    public static Brush Stripe(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(IMRCRaceControl.Models.ClassPrintColors.Safe(hex));
        return new SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)((color.R+510)/3), (byte)((color.G+510)/3), (byte)((color.B+510)/3)));
    }
}
