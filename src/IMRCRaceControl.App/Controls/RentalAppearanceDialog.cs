using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Controls;
internal sealed class RentalAppearanceDialog : Window
{
    private static readonly string[] Styles = ["Transponder", "ColorNumber", "Custom"];
    private static readonly string[] PrintFormats = ["Nickname", "NicknameAndNumber", "Number"];
    private readonly ComboBox _style = new(), _color = new(), _printFormat = new();
    private readonly TextBox _pattern = new();
    private readonly Slider _printSize = new() { Minimum = 8, Maximum = 18, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly CheckBox _bold = new() { Content = "Bold rental labels on printouts" }, _printColor = new() { Content = "Use rental label color on printouts" };
    public RentalAppearanceDialog(AppSettings settings)
    {
        var state = settings.RentalTransponders;
        Title = "Rental naming and print settings"; Width = 590; MaxHeight = 780; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "New rental defaults", FontSize = 24, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "These choices apply every time you add a transponder. Existing nicknames are edited separately.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,10) });
        void Field(string text, UIElement control) { panel.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0,8,0,4) }); panel.Children.Add(control); }
        _style.ItemsSource = new[] { "Transponder number***", "Color + next number", "My custom nickname pattern" };
        _style.SelectedIndex = Math.Max(0, Array.IndexOf(Styles, state.NewNicknameStyle));
        _color.ItemsSource = new[] { "Random" }.Concat(RentalTransponderRegistry.Colors).ToList(); _color.SelectedItem = state.NewLabelColor;
        _pattern.Text = state.CustomNicknamePattern;
        Field("Nickname style for each new rental", _style); Field("Label color (Random chooses a color when added)", _color); Field("Custom pattern", _pattern);
        panel.Children.Add(new TextBlock { Text = "Use {n} for the next unused label number, {number} for the transponder ID, and {color} for its color. Plain text automatically gets # plus the next number.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,5,0,0) });
        var next = new TextBlock { Text = $"Next unused label number: {RentalTransponderRegistry.NextLabelNumber(state)} (starts at 1, unique across all colors)", Margin = new Thickness(0,10,0,0), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(next);
        panel.Children.Add(new Separator { Margin = new Thickness(0,16,0,12) });
        panel.Children.Add(new TextBlock { Text = "Rental appearance on printouts", FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Rental transponders are identified as Rental on lineups and reports. These options do not change the inventory display.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,6,0,4) });
        _printFormat.ItemsSource = new[] { "Rental: nickname", "Rental: nickname (transponder number)", "Rental: transponder number" };
        _printFormat.SelectedIndex = Math.Max(0, Array.IndexOf(PrintFormats, state.PrintFormat));
        _printSize.Value = state.PrintFontSize; _bold.IsChecked = state.PrintBold; _printColor.IsChecked = state.PrintInColor;
        Field("Printed label format", _printFormat); Field("Printed rental font size", _printSize); panel.Children.Add(_bold); panel.Children.Add(_printColor);
        var example = new TextBlock { Style = null, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12), Foreground = Brushes.Black };
        panel.Children.Add(new Border { Background = Brushes.White, Child = example, Margin = new Thickness(0,10,0,0) });
        void Preview()
        {
            _pattern.IsEnabled = _style.SelectedIndex == 2; _color.IsEnabled = _style.SelectedIndex != 0;
            example.Text = _printFormat.SelectedIndex switch { 1 => "Rental: Red #1 (1234567)", 2 => "Rental: 1234567", _ => "Rental: Red #1" };
            example.FontSize = _printSize.Value; example.FontWeight = _bold.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            example.Foreground = _printColor.IsChecked == true ? new SolidColorBrush(Color.FromRgb(165, 28, 41)) : Brushes.Black;
        }
        _style.SelectionChanged += (_,_) => Preview(); _printFormat.SelectionChanged += (_,_) => Preview(); _printSize.ValueChanged += (_,_) => Preview();
        _bold.Checked += (_,_) => Preview(); _bold.Unchecked += (_,_) => Preview(); _printColor.Checked += (_,_) => Preview(); _printColor.Unchecked += (_,_) => Preview(); Preview();
        var error = new TextBlock { Margin = new Thickness(0,8,0,0), TextWrapping = TextWrapping.Wrap }; error.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0,0,8,0) });
        var save = new Button { Content = "Save defaults", IsDefault = true }; buttons.Children.Add(save);
        save.Click += (_,_) => { if (_style.SelectedIndex == 2 && string.IsNullOrWhiteSpace(_pattern.Text)) { error.Text = "Enter a custom nickname pattern."; return; } DialogResult = true; };
        panel.Children.Add(buttons); Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    public void Apply(AppSettings settings)
    {
        var state = settings.RentalTransponders;
        state.NewNicknameStyle = Styles[Math.Max(0, _style.SelectedIndex)]; state.NewLabelColor = _color.SelectedItem?.ToString() ?? "Random";
        state.CustomNicknamePattern = _pattern.Text.Trim(); state.PrintFormat = PrintFormats[Math.Max(0, _printFormat.SelectedIndex)];
        state.PrintFontSize = _printSize.Value; state.PrintBold = _bold.IsChecked == true; state.PrintInColor = _printColor.IsChecked == true;
    }
}
