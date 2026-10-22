using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using IMRCRaceControl.Models;
namespace IMRCRaceControl.Dialogs;
public sealed class SeriesAdjustmentDialog : Window
{
    private readonly ComboBox _week = new() { DisplayMemberPath="EventName" };
    private readonly TextBox _amount = new();
    private readonly TextBox _note = new() { AcceptsReturn=true, TextWrapping=TextWrapping.Wrap, Height=85 };
    public decimal Amount { get; private set; }
    public string Note => _note.Text.Trim();
    public string EventId => (_week.SelectedItem as SeriesWeekScore)?.EventId ?? "";
    public string EventName => (_week.SelectedItem as SeriesWeekScore)?.EventName ?? "Series total";
    public SeriesAdjustmentDialog(SeriesStandingRow row)
    {
        Title=$"Points adjustment — {row.DriverName}"; Width=490; SizeToContent=SizeToContent.Height; ResizeMode=ResizeMode.NoResize; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel { Margin=new Thickness(18) }; Content=panel;
        panel.Children.Add(new TextBlock { Text=$"{row.DriverName} • Current points: {row.Points:0.##}", FontSize=18 });
        panel.Children.Add(new TextBlock { Text="Apply to series total or a specific event", Margin=new Thickness(0,12,0,4) });
        _week.ItemsSource=new[] { new SeriesWeekScore { EventName="Series total (always counted)" } }.Concat(row.Weeks.Where(w=>!w.Pending)); _week.SelectedIndex=0; panel.Children.Add(_week);
        panel.Children.Add(new TextBlock { Text="Points to add / subtract (example: 5 or -5)", Margin=new Thickness(0,10,0,4) }); panel.Children.Add(_amount);
        panel.Children.Add(new TextBlock { Text="Reason / note (required)", Margin=new Thickness(0,10,0,4) }); panel.Children.Add(_note);
        panel.Children.Add(new TextBlock { Text="Event adjustments are included before choosing dropped weeks. Series-total adjustments always count.", TextWrapping=TextWrapping.Wrap });
        var buttons=new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right };
        var cancel=new Button { Content="CANCEL", IsCancel=true }; var save=new Button { Content="SAVE ADJUSTMENT" };
        save.Click+=(_,_)=> { if (!decimal.TryParse(_amount.Text,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var amount) || Math.Abs(amount)>1000000 || amount==0 || Note.Length==0) { MessageBox.Show(this,"Enter a non-zero points adjustment and a reason."); return; } Amount=amount; DialogResult=true; };
        buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons);
    }
}
