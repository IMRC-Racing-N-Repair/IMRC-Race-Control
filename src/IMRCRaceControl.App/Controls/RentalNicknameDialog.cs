using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Controls;
internal sealed class RentalNicknameDialog : Window
{
    private readonly ComboBox _color = new();
    private readonly TextBox _nickname = new();
    private readonly CheckBox _automatic = new() { Content = "Use the color and number as the nickname" };
    public string LabelColor { get; private set; }
    public int LabelNumber { get; }
    public string CustomNickname => _automatic.IsChecked == true ? "" : _nickname.Text;
    public RentalNicknameDialog(AppSettings settings, RentalTransponder rental)
    {
        LabelColor = rental.Color; LabelNumber = rental.LabelNumber > 0 ? rental.LabelNumber : RentalTransponderRegistry.NextLabelNumber(settings.RentalTransponders, rental.Id);
        Title = "Edit rental nickname"; Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = $"Transponder {rental.Number}", FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"Rental label number: {LabelNumber}", Margin = new Thickness(0,12,0,10) });
        _color.ItemsSource = new[] { "Default", "Random" }.Concat(RentalTransponderRegistry.Colors).ToList(); _color.SelectedItem = rental.Color; panel.Children.Add(_color);
        _nickname.Text = rental.Nickname;
        _automatic.IsChecked = rental.Color != "Default" && rental.Nickname == $"{rental.Color} #{rental.LabelNumber}";
        _automatic.Margin = new Thickness(0,10,0,6); panel.Children.Add(_automatic); panel.Children.Add(_nickname);
        void Toggle() => _nickname.IsEnabled = _automatic.IsChecked != true;
        _automatic.Checked += (_,_) => Toggle(); _automatic.Unchecked += (_,_) => Toggle(); Toggle();
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,0) }; error.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,14,0,0) };
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0,0,8,0) }); var save = new Button { Content = "Save nickname", IsDefault = true }; buttons.Children.Add(save);
        save.Click += (_,_) =>
        {
            try
            {
                if (_automatic.IsChecked != true && string.IsNullOrWhiteSpace(_nickname.Text)) throw new InvalidOperationException("Enter a nickname.");
                LabelColor = _color.SelectedItem?.ToString() ?? "Default";
                if (LabelColor == "Random") LabelColor = RentalTransponderRegistry.Colors[Random.Shared.Next(RentalTransponderRegistry.Colors.Length)];
                var copy = new AppSettings { RentalTransponders = JsonSerializer.Deserialize<RentalTransponderState>(JsonSerializer.Serialize(settings.RentalTransponders))! };
                RentalTransponderRegistry.SetLabel(copy, rental.Id, LabelColor, LabelNumber, CustomNickname); DialogResult = true;
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        panel.Children.Add(buttons); Content = panel;
    }
}
