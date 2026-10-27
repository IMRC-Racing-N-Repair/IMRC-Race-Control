using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using Microsoft.Win32;

namespace IMRCRaceControl.Dialogs;

public sealed class CompletedRaceExportDialog : Window
{
    private sealed record DriverChoice(int? Car, string Label);
    private readonly ComboBox _events = new() { DisplayMemberPath = "Name" };
    private readonly ComboBox _races = new() { DisplayMemberPath = "Label" };
    private readonly ComboBox _drivers = new() { DisplayMemberPath = "Label" };
    private readonly Button _print = new() { Content = "PRINT / PDF" };
    private readonly Button _export = new() { Content = "EXPORT CSV" };
    private sealed record RaceChoice(EventSessionDefinition Session)
    {
        public string Label => $"{Session.RoundLabel} • {Session.DashboardDisplay}";
    }

    public CompletedRaceExportDialog(IReadOnlyList<RaceDayEventDefinition> events, RaceDayEventDefinition? active, EventSessionDefinition? loaded)
    {
        Title = "Completed race reports"; Width = 620; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Margin = new Thickness(18) }; Content = body;
        body.Children.Add(new TextBlock { Text = "COMPLETED RACE • REPORTS", FontSize = 19, FontWeight = FontWeights.Bold });
        body.Children.Add(new TextBlock { Text = "Results, lap-by-lap analysis and prior qualifying order. Print, save a PDF, or export CSV.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,12) });
        void Field(string label, Control control) { body.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0,8,0,2) }); body.Children.Add(control); }
        Field("Event", _events); Field("Completed race", _races); Field("Drivers", _drivers);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,8) }; body.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_print); buttons.Children.Add(_export); body.Children.Add(buttons);
        _events.SelectionChanged += (_,_) =>
        {
            var ev = _events.SelectedItem as RaceDayEventDefinition;
            var races = RaceSchedule.DisplayOrder(ev?.Sessions.Where(s => s.ProgressStatus == EventSessionStatus.Completed) ?? []).Select(s => new RaceChoice(s)).ToList();
            _races.ItemsSource = races;
            _races.SelectedItem = races.FirstOrDefault(r => r.Session.Id == loaded?.Id) ?? races.FirstOrDefault();
            status.Text = races.Count == 0 ? "No completed races in this event yet." : "Results first, then position / lap time with pace beneath, followed by qualifying order before the race. Larger fields split into balanced reports of up to 8 drivers each.";
            _print.IsEnabled = _export.IsEnabled = races.Count > 0;
        };
        _races.SelectionChanged += (_,_) =>
        {
            var session = (_races.SelectedItem as RaceChoice)?.Session;
            var choices = new List<DriverChoice> { new(null, "All drivers in this race") };
            if (session is not null)
            {
                // Include recorded cars even if the event roster was edited after racing.
                var cars = session.Entrants.Select(e => e.CarNumber).Concat(session.Results.Select(r => r.CarNumber)).Concat(session.Laps.Select(l => l.CarNumber)).Concat(session.Breakouts.Select(b=>b.CarNumber)).Distinct().Order();
                foreach (var car in cars)
                {
                    var name = session.Entrants.FirstOrDefault(e => e.CarNumber == car)?.DisplayName ?? session.Results.FirstOrDefault(r => r.CarNumber == car)?.DriverName ?? session.Laps.FirstOrDefault(l => l.CarNumber == car)?.DriverName ?? session.Breakouts.First(b=>b.CarNumber==car).DriverName;
                    choices.Add(new(car, $"#{car} • {name}"));
                }
            }
            _drivers.ItemsSource = choices; _drivers.SelectedIndex = 0;
        };
        RaceReport Report() => RaceReports.CompletedRace((RaceDayEventDefinition)_events.SelectedItem, ((RaceChoice)_races.SelectedItem).Session, (_drivers.SelectedItem as DriverChoice)?.Car);
        _print.Click += (_,_) => Run(() => new ReportPrintDialog(Report()) { Owner = this }.ShowDialog());
        _export.Click += (_,_) => Run(() =>
        {
            var session = ((RaceChoice)_races.SelectedItem).Session;
            var save = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", DefaultExt = ".csv", FileName = $"Race-{session.RaceNumber}-{session.RoundLabel}-Laps.csv" };
            if (save.ShowDialog(this) != true) return;
            File.WriteAllText(save.FileName, RaceReports.LapReportCsv(Report()), new UTF8Encoding(true));
            status.Text = "Race report exported successfully.";
        });
        void Run(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, "Could not export this race: " + ex.Message, "Race export", MessageBoxButton.OK, MessageBoxImage.Warning); } }
        _events.ItemsSource = events.OrderByDescending(e => e.EventDate).ToList();
        _events.SelectedItem = active ?? events.FirstOrDefault(e => e.Sessions.Any(s => s.ProgressStatus == EventSessionStatus.Completed)) ?? events.FirstOrDefault();
        if (_events.SelectedItem is null) { _print.IsEnabled = _export.IsEnabled = false; status.Text = "Create an event and complete a race to export lap times."; }
    }
}
