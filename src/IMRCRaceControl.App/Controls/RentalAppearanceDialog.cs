using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Controls;
internal sealed class RentalAppearanceDialog : Window
{
    private readonly ComboBox _color = new();
    private readonly TextBox _number = new(), _nickname = new();
    private readonly CheckBox _custom = new() { Content = "Use my own custom nickname" };
    private readonly Slider _size = new() { Minimum = 12, Maximum = 30, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly Slider _row = new() { Minimum = 32, Maximum = 76, TickFrequency = 2, IsSnapToTickEnabled = true };
    public string Color => _color.SelectedItem?.ToString() ?? "Default";
    public int Number => int.TryParse(_number.Text, out var n) ? n : 0;
    public string CustomNickname => _custom.IsChecked == true ? _nickname.Text : "";
    public double LabelSize => _size.Value;
    public double RowSize => _row.Value;
    public RentalAppearanceDialog(AppSettings settings, RentalTransponder? rental)
    {
        Title = "Rental transponder appearance"; Width = 530; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "Rental display settings", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,18) });
        void Field(string title, UIElement control)
        {
            panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0,10,0,4) }); panel.Children.Add(control);
        }
        _size.Value = settings.RentalTransponders.LabelFontSize; _row.Value = settings.RentalTransponders.RowHeight;
        Field("Inventory text size", _size); Field("Inventory row height", _row);
        panel.Children.Add(new Separator { Margin = new Thickness(0,16,0,12) });
        panel.Children.Add(new TextBlock { Text = rental is null ? "Select a rental first to edit its label." : $"Label for transponder {rental.Number}", FontSize = 18, FontWeight = FontWeights.SemiBold });
        _color.ItemsSource = new[] { "Default" }.Concat(RentalTransponderRegistry.Colors).ToList();
        _color.SelectedItem = rental?.Color ?? "Default";
        _number.Text = (rental?.LabelNumber is > 0 ? rental.LabelNumber : 1).ToString();
        _nickname.Text = rental?.Nickname ?? "";
        var automatic = rental is null ? "" : rental.Color == "Default" ? RentalTransponderRegistry.DefaultNickname(rental.Number) : $"{rental.Color} #{rental.LabelNumber}";
        _custom.IsChecked = rental is not null && rental.Nickname != automatic;
        Field("Label color", _color); Field("Label number (Red #1, Black #5, etc.)", _number);
        panel.Children.Add(_custom); panel.Children.Add(_nickname);
        var random = new Button { Content = "Choose an unused random color / number", Margin = new Thickness(0,10,0,0), IsEnabled = rental is not null };
        random.Click += (_,_) => { if (rental is null) return; var label = RentalTransponderRegistry.RandomLabel(settings, rental.Id); _color.SelectedItem = label.Color; _number.Text = label.Number.ToString(); _custom.IsChecked = false; };
        panel.Children.Add(random);
        _color.IsEnabled = _number.IsEnabled = _custom.IsEnabled = rental is not null;
        void EnableNickname() => _nickname.IsEnabled = rental is not null && _custom.IsChecked == true;
        _custom.Checked += (_,_) => EnableNickname(); _custom.Unchecked += (_,_) => EnableNickname(); EnableNickname();
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,0) }; error.SetResourceReference(TextBlock.ForegroundProperty,"AccentBrush"); panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,14,0,0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0,0,8,0) };
        var save = new Button { Content = "Save appearance", IsDefault = true };
        save.Click += (_,_) =>
        {
            try
            {
                if (rental is not null)
                {
                    if (_custom.IsChecked == true && string.IsNullOrWhiteSpace(_nickname.Text)) throw new InvalidOperationException("Enter a custom nickname or turn off the custom nickname option.");
                    var test = new AppSettings { RentalTransponders = JsonSerializer.Deserialize<RentalTransponderState>(JsonSerializer.Serialize(settings.RentalTransponders))! };
                    RentalTransponderRegistry.SetLabel(test, rental.Id, Color, Number, CustomNickname);
                }
                DialogResult = true;
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons);
        Content = panel;
    }
}
