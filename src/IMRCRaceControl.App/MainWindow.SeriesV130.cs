using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using IMRCRaceControl.Core;
using IMRCRaceControl.Dialogs;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private bool _loadingSeries;
    private sealed record SeriesOption<T>(T Value, string Label);

    private void LoadSeriesFromSettings()
    {
        SeriesDefinitions.Clear();
        foreach (var series in _settings.Series) SeriesDefinitions.Add(series);
        _activeSeries = SeriesDefinitions.FirstOrDefault(s => s.Id == _settings.LastSeriesId) ?? SeriesDefinitions.FirstOrDefault();
        _loadingSeries = true;
        SeriesSelectCombo.SelectedItem = _activeSeries;
        _loadingSeries = false;
        LoadSeriesEditor(_activeSeries);
    }

    private async void SeriesSelectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSeries || _suppressUiEvents) return;
        var next = SeriesSelectCombo.SelectedItem as RaceSeriesDefinition;
        if (_activeSeries is not null && next != _activeSeries)
        {
            if (!TryReadSeriesEditor(_activeSeries, true))
            {
                _loadingSeries = true; SeriesSelectCombo.SelectedItem = _activeSeries; _loadingSeries = false; return;
            }
            await CaptureAndSaveSettingsAsync();
        }
        _activeSeries = next;
        LoadSeriesEditor(next);
    }

    private async void NewSeries_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSeries is not null && !TryReadSeriesEditor(_activeSeries, true)) return;
        var year = DateTime.Today.Year;
        var series = new RaceSeriesDefinition { Name = $"Series {year}", StartDate = new DateTime(year,1,1), EndDate = new DateTime(year,12,31), PointsSettings = new() };
        SeriesDefinitions.Add(series);
        _activeSeries = series;
        _loadingSeries = true; SeriesSelectCombo.SelectedItem = series; _loadingSeries = false;
        LoadSeriesEditor(series);
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = "Series created. Set the dates, points and events, then SAVE SERIES.";
    }

    private async void SaveSeries_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSeries is null || !TryReadSeriesEditor(_activeSeries, true)) return;
        SeriesSelectCombo.Items.Refresh();
        RefreshSeriesEventChoices(); RefreshSeriesClassChoices(); RecalculateSeriesStandings();
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"Series '{_activeSeries.Name}' saved.";
    }

    private async void DeleteSeries_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSeries is null || MessageBox.Show(this, $"Delete series '{_activeSeries.Name}'? Event results will remain.", "Delete series", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _loadingSeries = true;
        SeriesDefinitions.Remove(_activeSeries);
        _activeSeries = SeriesDefinitions.FirstOrDefault(); SeriesSelectCombo.SelectedItem = _activeSeries;
        _loadingSeries = false;
        LoadSeriesEditor(_activeSeries); await CaptureAndSaveSettingsAsync();
    }

    private void LoadSeriesEditor(RaceSeriesDefinition? series)
    {
        _loadingSeries = true;
        try
        {
            SeriesPages.IsEnabled = series is not null;
            series ??= new RaceSeriesDefinition();
            SeriesNameText.Text = series.Name;
            SeriesStartDateText.Text = series.StartDate.ToString("MM/dd/yyyy");
            SeriesEndDateText.Text = series.EndDate.ToString("MM/dd/yyyy");
            SeriesMaximumText.Text = series.MaximumEvents.ToString();
            SeriesBestText.Text = series.BestEventCount.ToString();
            SeriesNotesText.Text = series.Notes;
            var p = series.EffectivePoints;
            LoadStage("Qual", p.Qualifying); LoadStage("Main", p.Mains);
            SeriesTqText.Text = p.TqBonus.ToString(CultureInfo.InvariantCulture);
            SeriesAttendanceText.Text = p.EventAttendancePoints.ToString(CultureInfo.InvariantCulture);
            SeriesQualModeCombo.ItemsSource = new[] { new SeriesOption<QualifyingPointsMode>(QualifyingPointsMode.OverallEvent,"Overall event qualifying rank"), new(QualifyingPointsMode.EveryRound,"Every qualifying round"), new(QualifyingPointsMode.BestRound,"Best qualifying round") };
            SeriesQualModeCombo.DisplayMemberPath = "Label"; SeriesQualModeCombo.SelectedValuePath = "Value"; SeriesQualModeCombo.SelectedValue = p.QualifyingMode;
            SeriesTieCombo.ItemsSource = new[] { new SeriesOption<SeriesTieBreak>(SeriesTieBreak.WinsThenBestFinish,"Most counted wins, then best counted finish"), new(SeriesTieBreak.BestWeek,"Highest single counted week"), new(SeriesTieBreak.MostEvents,"Most events attended") };
            SeriesTieCombo.DisplayMemberPath = "Label"; SeriesTieCombo.SelectedValuePath = "Value"; SeriesTieCombo.SelectedValue = p.TieBreak;
            SeriesLegacyFallbackCheck.IsChecked = p.LegacyQualifyingFallback;
            RefreshSeriesEventChoices(); RefreshSeriesClassChoices();
        }
        finally { _loadingSeries = false; }
        RecalculateSeriesStandings();
    }

    private TextBox SeriesField(string name) => (TextBox)FindName(name);
    private void LoadStage(string prefix, StagePointsSettings p)
    {
        ((CheckBox)FindName(prefix+"Enabled")).IsChecked = p.Enabled;
        SeriesField(prefix+"PositionText").Text = p.PositionPoints;
        var values = new Dictionary<string, decimal> { ["Unlisted"]=p.UnlistedPositionPoints, ["Lap"]=p.PointsPerLap, ["Participation"]=p.ParticipationPoints, ["Win"]=p.WinBonus, ["Fast"]=p.FastLapBonus, ["Minimum"]=p.MinimumLaps };
        foreach (var v in values) SeriesField(prefix+v.Key+"Text").Text = v.Value.ToString(CultureInfo.InvariantCulture);
    }
    private decimal ReadNumber(TextBox box, string label)
    {
        if (!decimal.TryParse(box.Text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) || n < 0 || n > 1000000) throw new ArgumentException($"{label}: enter a number from 0 to 1,000,000 (use a dot for decimals).");
        return n;
    }
    private int ReadWhole(TextBox box, string label)
    {
        var n = ReadNumber(box,label);
        if (n != decimal.Truncate(n)) throw new ArgumentException($"{label} must be a whole number.");
        return (int)n;
    }
    private StagePointsSettings ReadStage(string prefix)
    {
        var schedule = SeriesField(prefix+"PositionText").Text.Trim();
        if (!SeriesScoring.TrySchedule(schedule, out _)) throw new ArgumentException($"{prefix} position points must be comma-separated non-negative numbers.");
        return new StagePointsSettings { Enabled = ((CheckBox)FindName(prefix+"Enabled")).IsChecked == true, PositionPoints = schedule,
            UnlistedPositionPoints = ReadNumber(SeriesField(prefix+"UnlistedText"),prefix+" unlisted positions"),
            PointsPerLap = ReadNumber(SeriesField(prefix+"LapText"),prefix+" points per lap"),
            ParticipationPoints = ReadNumber(SeriesField(prefix+"ParticipationText"),prefix+" participation"),
            WinBonus = ReadNumber(SeriesField(prefix+"WinText"),prefix+" win bonus"),
            FastLapBonus = ReadNumber(SeriesField(prefix+"FastText"),prefix+" fast lap"),
            MinimumLaps = ReadWhole(SeriesField(prefix+"MinimumText"),prefix+" minimum laps") };
    }

    private bool TryReadSeriesEditor(RaceSeriesDefinition series, bool showWarnings)
    {
        if (_loadingSeries) return true;
        try
        {
            if (string.IsNullOrWhiteSpace(SeriesNameText.Text)) throw new ArgumentException("Enter a series name.");
            if (!TryParseEventDate(SeriesStartDateText.Text,out var start) || !TryParseEventDate(SeriesEndDateText.Text,out var end) || end < start) throw new ArgumentException("Enter a valid start and end date (MM/DD/YYYY), with the end on or after the start.");
            var max = ReadWhole(SeriesMaximumText,"Events allowed");
            var best = ReadWhole(SeriesBestText,"Best event count");
            if (max > 0 && best > max) throw new ArgumentException("Best event count cannot exceed events allowed.");
            var ids = SeriesEventChoices.Where(c => c.Included).Select(c => c.EventId).Distinct().ToList();
            if (max > 0 && ids.Count > max) throw new ArgumentException($"Only {max} events are allowed. Remove an event or increase the limit.");
            if (showWarnings && Events.Any(ev => ids.Contains(ev.Id) && (ev.EventDate.Date < start.Date || ev.EventDate.Date > end.Date))) throw new ArgumentException("A selected event is outside the series dates. Extend the dates or uncheck that event.");
            var points = new SeriesPointsSettings { Qualifying = ReadStage("Qual"), Mains = ReadStage("Main"),
                TqBonus = ReadNumber(SeriesTqText,"TQ bonus"), EventAttendancePoints = ReadNumber(SeriesAttendanceText,"Attendance points"),
                QualifyingMode = SeriesQualModeCombo.SelectedValue is QualifyingPointsMode mode ? mode : QualifyingPointsMode.OverallEvent,
                TieBreak = SeriesTieCombo.SelectedValue is SeriesTieBreak tie ? tie : SeriesTieBreak.WinsThenBestFinish,
                LegacyQualifyingFallback = SeriesLegacyFallbackCheck.IsChecked == true };
            series.Name = SeriesNameText.Text.Trim(); series.StartDate = start; series.EndDate = end;
            series.MaximumEvents = max; series.BestEventCount = best; series.Notes = SeriesNotesText.Text.Trim();
            series.PointsSettings = points; series.PointsCsv = points.Mains.PositionPoints; series.EventIds = ids;
            return true;
        }
        catch (ArgumentException ex)
        {
            if (showWarnings) MessageBox.Show(this,ex.Message,"Series settings",MessageBoxButton.OK,MessageBoxImage.Information);
            return false;
        }
    }

    private void RefreshSeriesEventChoices()
    {
        SeriesEventChoices.Clear();
        if (_activeSeries is null) return;
        foreach (var ev in Events.Where(ev => _activeSeries.EventIds.Contains(ev.Id) || ev.EventDate.Date >= _activeSeries.StartDate.Date && ev.EventDate.Date <= _activeSeries.EndDate.Date).OrderBy(ev => ev.EventDate))
            SeriesEventChoices.Add(new SeriesEventChoice { EventId=ev.Id, EventName=ev.Name, EventDate=ev.EventDate, Included=_activeSeries.EventIds.Contains(ev.Id),
                Status = ev.EventDate.Date < _activeSeries.StartDate.Date || ev.EventDate.Date > _activeSeries.EndDate.Date ? "OUTSIDE DATES" : ev.Status == EventStatus.Completed ? "Completed" : "Pending" });
    }
    private void RefreshSeriesEvents_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSeries is null || !TryReadSeriesEditor(_activeSeries,false)) { MessageBox.Show(this,"Check the Series information and points fields first."); return; }
        RefreshSeriesEventChoices(); RefreshSeriesClassChoices(); RecalculateSeriesStandings();
    }
    private void SeriesEventCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSeries is null) return;
        if (!TryReadSeriesEditor(_activeSeries,true)) { RefreshSeriesEventChoices(); return; }
        RefreshSeriesClassChoices(); RecalculateSeriesStandings();
    }
    private void RefreshSeriesClassChoices()
    {
        var prior = (SeriesClassCombo.SelectedItem as SeriesClassChoice)?.ClassId;
        SeriesClassChoices.Clear();
        if (_activeSeries is null) return;
        var ids = Events.Where(ev => _activeSeries.EventIds.Contains(ev.Id)).SelectMany(ev => ev.ClassIds.Concat(ev.Sessions.Select(s => s.ClassId)))
            .Concat(_activeSeries.Adjustments.Select(a => a.ClassId)).Distinct();
        foreach (var id in ids)
            SeriesClassChoices.Add(new SeriesClassChoice(id, Classes.FirstOrDefault(c=>c.Id==id)?.Name ?? Events.SelectMany(ev=>ev.Sessions).FirstOrDefault(s=>s.ClassId==id)?.ClassName ?? "Class"));
        SeriesClassCombo.SelectedItem = SeriesClassChoices.FirstOrDefault(c=>c.ClassId==prior) ?? SeriesClassChoices.FirstOrDefault();
    }
    private void SeriesClassCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingSeries) RecalculateSeriesStandings();
    }
    private void SeriesPages_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSeries || e.Source != SeriesPages || _activeSeries is null) return;
        if (TryReadSeriesEditor(_activeSeries,true)) { RefreshSeriesEventChoices(); RefreshSeriesClassChoices(); RecalculateSeriesStandings(); }
        else SeriesStandingsInfoText.Text = "Settings were not applied. Correct the indicated fields and save or refresh standings.";
    }
    private void RefreshSeriesStandings_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSeries is not null && TryReadSeriesEditor(_activeSeries,true)) RecalculateSeriesStandings();
    }
    private void RecalculateSeriesStandings()
    {
        var driverId = (SeriesStandingsGrid.SelectedItem as SeriesStandingRow)?.DriverId;
        SeriesStandings.Clear(); SeriesWeeksGrid.ItemsSource = null; SeriesAdjustmentsGrid.ItemsSource = null;
        SeriesStandingsGrid.Columns.Clear();
        if (_activeSeries is null || SeriesClassCombo.SelectedItem is not SeriesClassChoice cls) { SeriesStandingsInfoText.Text="Select a series, events and class."; return; }
        void Column(string title, string binding, double width) => SeriesStandingsGrid.Columns.Add(new DataGridTextColumn { Header=title, Binding=new Binding(binding), Width=width });
        Column("POS","Position",45); Column("DRIVER","DriverName",160); Column("POINTS","Points",80);
        Column("ATTENDED","Events",80); Column("COUNTED","CountedEvents",75); Column("DROPPED PTS","DroppedPoints",95); Column("TOTAL ADJUST","ManualPoints",100);
        foreach (var ev in SeriesScoring.SelectedEvents(_activeSeries, Events)) Column($"{ev.EventDate:MM/dd}\n{ev.Name}",$"WeekDisplay[{ev.Id}]",130);
        foreach (var row in SeriesScoring.Calculate(_activeSeries,Events,cls.ClassId,Drivers)) SeriesStandings.Add(row);
        SeriesStandingsGrid.SelectedItem = SeriesStandings.FirstOrDefault(r=>r.DriverId==driverId) ?? SeriesStandings.FirstOrDefault();
        var selected = SeriesScoring.SelectedEvents(_activeSeries,Events);
        SeriesStandingsInfoText.Text = $"{cls.ClassName} • {selected.Count(e=>e.Status==EventStatus.Completed)} completed / {selected.Count} selected • " +
            (_activeSeries.BestEventCount > 0 ? $"Best {_activeSeries.BestEventCount} events count" : "All events count") + " • Registered/raced drivers in this class only. Dropped weeks show in parentheses. Pending events do not score.";
    }
    private void SeriesStanding_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = SeriesStandingsGrid.SelectedItem as SeriesStandingRow;
        SeriesWeeksGrid.ItemsSource = row?.Weeks;
        SeriesAdjustmentsGrid.ItemsSource = _activeSeries?.Adjustments.Where(a=>a.DriverId==row?.DriverId && a.ClassId==(SeriesClassCombo.SelectedItem as SeriesClassChoice)?.ClassId).OrderBy(a=>a.CreatedAt).ToList();
    }
    private async void AdjustSeriesPoints_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSeries is null || SeriesStandingsGrid.SelectedItem is not SeriesStandingRow row || SeriesClassCombo.SelectedItem is not SeriesClassChoice cls) return;
        if (!TryReadSeriesEditor(_activeSeries,true)) return;
        var dialog = new SeriesAdjustmentDialog(row) { Owner=this };
        if (dialog.ShowDialog()!=true) return;
        _activeSeries.Adjustments.Add(new SeriesPointsAdjustment { DriverId=row.DriverId, DriverName=row.DriverName, ClassId=cls.ClassId,
            EventId=dialog.EventId, EventName=dialog.EventName, Amount=dialog.Amount, Note=dialog.Note });
        RecalculateSeriesStandings(); await CaptureAndSaveSettingsAsync();
    }
    private async void RemoveSeriesAdjustment_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSeries is null || SeriesAdjustmentsGrid.SelectedItem is not SeriesPointsAdjustment adjustment) return;
        if (MessageBox.Show(this,$"Remove {adjustment.Amount:+0.##;-0.##;0} points adjustment?\n{adjustment.Note}","Remove adjustment",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
        _activeSeries.Adjustments.Remove(adjustment); RecalculateSeriesStandings(); await CaptureAndSaveSettingsAsync();
    }
    private IReadOnlyList<(string DriverId,string DriverName)> GetEventClassFinishOrder(RaceDayEventDefinition ev,string classId)
    {
        var mains=SeriesScoring.MainOrder(ev,classId);
        return mains.Count > 0 ? mains : SeriesScoring.QualifyingOrder(ev,classId);
    }
}
