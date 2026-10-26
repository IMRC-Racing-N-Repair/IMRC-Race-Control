using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using IMRCRaceControl.Core;

namespace IMRCRaceControl.Dialogs;
internal static class ReportTextStyling
{
    public static void Append(InlineCollection inlines, string value, RaceReport report)
    {
        var position = 0;
        while (position < value.Length)
        {
            var found = report.TextStyles.Keys.Select(key => (Key: key, At: value.IndexOf(key, position, StringComparison.Ordinal)))
                .Where(x => x.At >= 0).OrderBy(x => x.At).ThenByDescending(x => x.Key.Length).FirstOrDefault();
            if (found.Key is null) { inlines.Add(new Run(value[position..])); break; }
            if (found.At > position) inlines.Add(new Run(value[position..found.At]));
            var style = report.TextStyles[found.Key];
            inlines.Add(new Run(found.Key) { FontSize = style.FontSize, FontWeight = style.Bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(style.Color)) });
            position = found.At + found.Key.Length;
        }
    }
    public static TextBlock Block(string value, RaceReport report, double size, bool bold, bool centered)
    {
        var block = new TextBlock { Style = null, FontFamily = new FontFamily("Segoe UI"), FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap, TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left };
        Append(block.Inlines, value, report); return block;
    }
    public static Paragraph Paragraph(string value, RaceReport report)
    {
        var paragraph = new Paragraph(); Append(paragraph.Inlines, value, report); return paragraph;
    }
}
