using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IMRCRaceControl.Controls;

public sealed class TrackRecordStrip : Border
{
    private readonly TextBlock _text = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55)),
        FontSize = 16, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };

    public TrackRecordStrip()
    {
        Child = _text;
        Background = new SolidColorBrush(Color.FromRgb(255, 213, 79));
        Padding = new Thickness(10, 6, 10, 6);
        Margin = new Thickness(0, 4, 0, 0);
        Visibility = Visibility.Collapsed;
    }

    public void Show(string? message)
    {
        _text.Text = message ?? "";
        Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }
}
