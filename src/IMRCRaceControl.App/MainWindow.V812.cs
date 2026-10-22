using System.Windows;
using System.Windows.Controls;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private int? _selectedDriverLapCarNumber;
    private void StandingsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StandingsGrid.SelectedItem is not StandingRow row)
            return;

        _selectedDriverLapCarNumber = row.CarNumber;

        var racer = SessionRacers.FirstOrDefault(r => r.CarNumber == row.CarNumber);
        if (racer is not null)
        {
            _suppressUiEvents = true;
            try { DriverLapDriverCombo.SelectedItem = racer; }
            finally { _suppressUiEvents = false; }
        }

        RefreshDriverLapPanel();
    }

    private void DriverLapPanelToggle_Click(object sender, RoutedEventArgs e) =>
        ToggleRaceSidePanel("LAPS");

    private void ResetDriverLapPanelForSession()
    {
        DriverLapRows.Clear();

        RacerEntry? target = null;
        if (_selectedDriverLapCarNumber.HasValue)
        {
            target = SessionRacers.FirstOrDefault(r =>
                r.Enabled && r.CarNumber == _selectedDriverLapCarNumber.Value);
        }

        target ??= SessionRacers.FirstOrDefault(r => r.Enabled);

        _suppressUiEvents = true;
        try { DriverLapDriverCombo.SelectedItem = target; }
        finally { _suppressUiEvents = false; }

        _selectedDriverLapCarNumber = target?.CarNumber;

        if (target is null)
        {
            DriverLapTitleText.Text = "Select a driver";
            DriverLapSummaryText.Text = "No racers are loaded.";
            StandingsGrid.SelectedItem = null;
            RefreshLapCorrectionAvailability();
            return;
        }

        RefreshDriverLapPanel();
    }

    private void RefreshDriverLapPanel()
    {
        if (_raceSidePanelCollapsed ||
            !string.Equals(_raceSidePanel, "LAPS", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var selectedKey = (DriverLapGrid.SelectedItem as DriverLapViewRow)?.RecordKey;
        DriverLapRows.Clear();

        if (!_selectedDriverLapCarNumber.HasValue)
        {
            DriverLapTitleText.Text = "Select a driver";
            DriverLapSummaryText.Text = "Choose a racer from the dropdown.";
            RefreshLapCorrectionAvailability();
            return;
        }

        var carNumber = _selectedDriverLapCarNumber.Value;
        var standing = Standings.FirstOrDefault(r => r.CarNumber == carNumber);
        var racer = SessionRacers.FirstOrDefault(r => r.CarNumber == carNumber);

        var driverName =
            standing?.DriverName ??
            racer?.DisplayName ??
            $"Car {carNumber}";

        IEnumerable<LapRecord> validLaps;
        IEnumerable<BreakoutRecord> breakouts;

        var useStoredSession =
            _loadedSession is not null &&
            _loadedSession.ProgressStatus == EventSessionStatus.Completed;

        if (useStoredSession)
        {
            validLaps = _loadedSession!.Laps.Where(l => l.CarNumber == carNumber);
            breakouts = _loadedSession.Breakouts.Where(b => b.CarNumber == carNumber);
        }
        else
        {
            validLaps = _raceEngine.Laps.Where(l => l.CarNumber == carNumber);
            breakouts = _raceEngine.Breakouts.Where(b => b.CarNumber == carNumber);
        }

        var valid = validLaps.OrderBy(l => l.Timestamp).ToList();
        var breakout = breakouts.OrderBy(b => b.Timestamp).ToList();

        var merged = new List<DriverLapViewRow>();

        merged.AddRange(valid.Select(l => new DriverLapViewRow
        {
            Sequence = l.Sequence,
            CarNumber = l.CarNumber,
            LapNumber = l.LapNumber,
            Timestamp = l.Timestamp,
            LapLabel = l.LapNumber.ToString(),
            LapTime = RaceEngine.FormatLap(l.LapTime),
            Elapsed = RaceEngine.FormatElapsed(l.Elapsed),
            Status = l.Finished ? "VALID • FINISH" : "VALID",
            Source = l.Source,
            IsBreakout = false
        }));

        merged.AddRange(breakout.Select(b => new DriverLapViewRow
        {
            Sequence = b.Sequence,
            CarNumber = b.CarNumber,
            LapNumber = b.AttemptedLapNumber,
            Timestamp = b.Timestamp,
            LapLabel = $"{b.AttemptedLapNumber}*",
            LapTime = RaceEngine.FormatLap(b.LapTime),
            Elapsed = RaceEngine.FormatElapsed(b.Elapsed),
            Status = $"BREAKOUT < {RaceEngine.FormatLap(b.MinimumLapTime)}",
            Source = b.Source,
            IsBreakout = true
        }));

        foreach (var row in merged.OrderBy(r => r.Timestamp))
            DriverLapRows.Add(row);

        var best = valid.Count == 0
            ? "—"
            : RaceEngine.FormatLap(valid.Min(l => l.LapTime));

        DriverLapTitleText.Text = $"CAR {carNumber} • {driverName}";
        DriverLapSummaryText.Text =
            $"{valid.Count} scored lap(s) • {breakout.Count} breakout(s) • Best {best}";

        DriverLapGrid.SelectedItem = selectedKey is null
            ? null
            : DriverLapRows.FirstOrDefault(r => r.RecordKey == selectedKey);

        RefreshLapCorrectionAvailability();
    }

    private void RefreshLapCorrectionAvailability()
    {
        var completed =
            _loadedSession is not null &&
            _loadedSession.ProgressStatus == EventSessionStatus.Completed &&
            !_resultAutoSaveInProgress &&
            _raceEngine.State is not RaceState.Armed and
            not RaceState.Running and
            not RaceState.TimeExpired;

        AddLapButton.IsEnabled = completed && _selectedDriverLapCarNumber.HasValue;
        ChangeLapButton.IsEnabled = completed && DriverLapGrid.SelectedItem is DriverLapViewRow;
        DeleteLapButton.IsEnabled = completed && DriverLapGrid.SelectedItem is DriverLapViewRow;

        LapCorrectionStatusText.Text = completed
            ? "Select a lap to CHANGE/DELETE. ADD inserts after the selected lap; with no selection it appends."
            : "Corrections are available after this race is complete.";
    }
}
