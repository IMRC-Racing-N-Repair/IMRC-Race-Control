using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IMRCRaceControl.Dialogs;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private UnknownTransponderWindow? _unknownTransponderWindow;

    public ObservableCollection<UnknownTransponderHit> UnknownTransponderHits { get; } = new();

    // -----------------------------------------------------------------
    // Unknown transponder queue / one-window workflow
    // -----------------------------------------------------------------

    private void QueueUnknownTransponder(
        string transponderId,
        string source,
        DateTimeOffset timestamp)
    {
        var id = transponderId.Trim();
        if (id.Length == 0)
            return;

        var existing = UnknownTransponderHits.FirstOrDefault(hit =>
            string.Equals(hit.TransponderId, id, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            existing = new UnknownTransponderHit
            {
                TransponderId = id,
                FirstSeen = timestamp
            };
            UnknownTransponderHits.Add(existing);
        }

        existing.AddCrossing(timestamp, source);

        FooterStatusText.Text =
            $"UNKNOWN TRANSPONDER {id} • {UnknownTransponderHits.Count} unassigned tag(s).";

        ShowUnknownTransponderWindow();
    }

    private void ShowUnknownTransponderWindow()
    {
        if (_unknownTransponderWindow is not null)
        {
            if (_unknownTransponderWindow.WindowState == WindowState.Minimized)
                _unknownTransponderWindow.WindowState = WindowState.Normal;
            return;
        }

        _unknownTransponderWindow = new UnknownTransponderWindow(this)
        {
            Owner = this
        };

        _unknownTransponderWindow.Closed += (_, _) =>
            _unknownTransponderWindow = null;

        _unknownTransponderWindow.Show();
    }

    internal async Task AssignUnknownTransponderAsync(
        UnknownTransponderHit hit,
        RacerEntry racer)
    {
        if (!UnknownTransponderHits.Contains(hit))
            return;

        if (SessionRacers.All(r => r.CarNumber != racer.CarNumber))
            return;

        var duplicateOwner = SessionRacers.FirstOrDefault(r =>
            r.CarNumber != racer.CarNumber &&
            SplitTransponders(r.TransponderId).Any(t =>
                string.Equals(t, hit.TransponderId, StringComparison.OrdinalIgnoreCase)));

        if (duplicateOwner is not null)
        {
            MessageBox.Show(this,
                $"{hit.TransponderId} is already assigned to {duplicateOwner.DisplayName} in this race.",
                "Duplicate transponder",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var crossings = hit.Crossings
            .OrderBy(c => c.Timestamp)
            .ToList();

        if (!await SetRaceTransponderAsync(
                racer,
                hit.TransponderId,
                persistPermanentAssignment: true))
        {
            return;
        }

        UnknownTransponderHits.Remove(hit);

        FooterStatusText.Text =
            $"{hit.TransponderId} assigned to {racer.DisplayName}. Replaying {crossings.Count} stored crossing(s).";

        foreach (var crossing in crossings)
        {
            RegisterPassingForCar(
                racer.CarNumber,
                $"{crossing.Source} • assigned",
                crossing.Timestamp);
        }
    }

    internal void ClearUnknownTransponder(UnknownTransponderHit hit)
    {
        if (UnknownTransponderHits.Remove(hit))
            FooterStatusText.Text = $"Cleared unknown transponder {hit.TransponderId}.";
    }

    internal void ClearAllUnknownTransponders()
    {
        var count = UnknownTransponderHits.Count;
        UnknownTransponderHits.Clear();
        FooterStatusText.Text = $"Cleared {count} unknown transponder entr{(count == 1 ? "y" : "ies")}.";
    }

    // -----------------------------------------------------------------
    // Race row right-click tools
    // -----------------------------------------------------------------

    private void StandingsGrid_TransponderDragOver(object sender, DragEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        e.Effects = e.Data.GetDataPresent(UnknownTransponderWindow.DragFormat) && row?.Item is StandingRow
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private async void StandingsGrid_TransponderDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(UnknownTransponderWindow.DragFormat) is not UnknownTransponderHit hit)
            return;
        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not StandingRow standing) return;
        var racer = SessionRacers.FirstOrDefault(r => r.CarNumber == standing.CarNumber);
        if (racer is not null) await AssignUnknownTransponderAsync(hit, racer);
    }

    private void StandingsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null)
            return;

        StandingsGrid.SelectedItem = row.Item;
        row.Focus();
    }

    private RacerEntry? SelectedRaceRacer()
    {
        if (StandingsGrid.SelectedItem is not StandingRow row)
            return null;

        return SessionRacers.FirstOrDefault(r => r.CarNumber == row.CarNumber);
    }

    private async void ChangeRaceTransponder_Click(object sender, RoutedEventArgs e)
    {
        var racer = SelectedRaceRacer();
        if (racer is null)
            return;

        var dialog = new TransponderPromptDialog(
            racer.DisplayName,
            racer.TransponderId)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return;

        if (!await SetRaceTransponderAsync(
                racer,
                dialog.Transponder,
                persistPermanentAssignment: true))
        {
            return;
        }

        FooterStatusText.Text = string.IsNullOrWhiteSpace(dialog.Transponder)
            ? $"{racer.DisplayName}'s transponder was cleared."
            : $"{racer.DisplayName}'s transponder changed to {dialog.Transponder}.";
    }

    private async void ClearRaceTransponder_Click(object sender, RoutedEventArgs e)
    {
        var racer = SelectedRaceRacer();
        if (racer is null)
            return;

        if (MessageBox.Show(this,
                $"Clear the transponder assigned to {racer.DisplayName}?",
                "Clear transponder",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        if (!await SetRaceTransponderAsync(
                racer,
                string.Empty,
                persistPermanentAssignment: true))
        {
            return;
        }

        FooterStatusText.Text = $"{racer.DisplayName}'s transponder was cleared.";
    }

    private async void RemoveRaceDriver_Click(object sender, RoutedEventArgs e)
    {
        var racer = SelectedRaceRacer();
        if (racer is null)
            return;

        if (!CanEditLoadedRaceRoster())
            return;

        if (MessageBox.Show(this,
                $"Remove {racer.DisplayName} from this race/heat?\n\nTheir event/class registration is retained; only this race roster is changed.",
                "Remove racer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        SessionRacers.Remove(racer);

        if (_loadedSession is not null)
        {
            _loadedSession.Entrants.RemoveAll(e =>
                string.Equals(e.DriverId, racer.DriverId, StringComparison.OrdinalIgnoreCase));
        }

        RenumberLiveRaceRoster();
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"{racer.DisplayName} removed from {_loadedSession?.Name ?? "the current race"}.";
    }

    private async void AddRaceDriver_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditLoadedRaceRoster())
            return;

        if (SessionRacers.Count >= 30)
        {
            MessageBox.Show(this,
                "This race roster is full. Add the racer to another heat/main.",
                "Add racer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var currentIds = SessionRacers
            .Select(r => r.DriverId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var available = Drivers
            .Where(d => !currentIds.Contains(d.Id))
            .OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var picker = new RaceDriverPickerDialog(available)
        {
            Owner = this
        };

        if (picker.ShowDialog() != true)
            return;

        DriverProfile? driver = picker.SelectedDriver;
        var transponder = picker.Transponder;

        if (picker.CreateNewDriver)
        {
            driver = ShowDriverEditor();
            if (driver is null)
                return;
        }

        if (driver is null)
            return;

        await AddDriverToLoadedRaceAsync(driver, transponder);
    }

    private bool CanEditLoadedRaceRoster()
    {
        if (_loadedSession?.ProgressStatus == EventSessionStatus.Completed || _recallMode)
        {
            MessageBox.Show(this,
                "A completed/recalled race roster is read-only.",
                "Race roster",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            MessageBox.Show(this,
                "Add/remove racers before START. Transponder changes can still be made while the race is running.",
                "Race roster",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        return true;
    }

    private async Task AddDriverToLoadedRaceAsync(
        DriverProfile driver,
        string transponder)
    {
        if (SessionRacers.Any(r =>
                string.Equals(r.DriverId, driver.Id, StringComparison.OrdinalIgnoreCase)))
        {
            FooterStatusText.Text = $"{driver.DisplayName} is already in this race.";
            return;
        }

        var cls = RaceClassCombo.SelectedItem as ClassDefinition
                  ?? Classes.FirstOrDefault(c => c.Id == _loadedSession?.ClassId);

        if (cls is null)
            return;

        if (!string.IsNullOrWhiteSpace(transponder))
        {
            var duplicate = SessionRacers.FirstOrDefault(r =>
                SplitTransponders(r.TransponderId).Any(t =>
                    string.Equals(t, transponder.Trim(), StringComparison.OrdinalIgnoreCase)));

            if (duplicate is not null)
            {
                MessageBox.Show(this,
                    $"{transponder.Trim()} is already assigned to {duplicate.DisplayName} in this race.",
                    "Duplicate transponder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        var racer = new RacerEntry
        {
            CarNumber = SessionRacers.Count + 1,
            Enabled = true,
            DriverId = driver.Id,
            DriverName = driver.FullName,
            Nickname = driver.Nickname,
            ClassId = cls.Id,
            ClassName = cls.Name,
            TransponderId = transponder.Trim()
        };

        SessionRacers.Add(racer);

        if (_loadedSession is not null)
        {
            _loadedSession.Entrants.Add(new SessionEntrant
            {
                CarNumber = racer.CarNumber,
                DriverId = driver.Id,
                DriverName = driver.FullName,
                Nickname = driver.Nickname,
                TransponderId = racer.TransponderId,
                SeedRank = 0
            });
        }

        var permanentAssignment = _driverAssignments.FirstOrDefault(a =>
            string.Equals(a.DriverId, driver.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.ClassId, cls.Id, StringComparison.OrdinalIgnoreCase));

        if (permanentAssignment is null)
        {
            permanentAssignment = new DriverClassAssignment
            {
                DriverId = driver.Id,
                ClassId = cls.Id,
                TransponderId = racer.TransponderId,
                SeedRank = 0
            };
            _driverAssignments.Add(permanentAssignment);
        }
        else if (!string.IsNullOrWhiteSpace(racer.TransponderId))
        {
            permanentAssignment.TransponderId = racer.TransponderId;
        }

        if (_activeEvent is not null)
        {
            var eventAssignment = _activeEvent.DriverAssignments.FirstOrDefault(a =>
                string.Equals(a.DriverId, driver.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.ClassId, cls.Id, StringComparison.OrdinalIgnoreCase));

            if (eventAssignment is null)
            {
                eventAssignment = new EventDriverAssignment
                {
                    DriverId = driver.Id,
                    ClassId = cls.Id,
                    TransponderId = racer.TransponderId,
                    SeedRank = 0
                };
                _activeEvent.DriverAssignments.Add(eventAssignment);
            }
            else if (!string.IsNullOrWhiteSpace(racer.TransponderId))
            {
                eventAssignment.TransponderId = racer.TransponderId;
            }
        }

        RenumberLiveRaceRoster();
        RefreshEventDriverChoices(driver.Id);
        await CaptureAndSaveSettingsAsync();

        FooterStatusText.Text = $"{driver.DisplayName} added to {_loadedSession?.Name ?? "the current race"}.";
    }

    private async Task<bool> SetRaceTransponderAsync(
        RacerEntry racer,
        string transponder,
        bool persistPermanentAssignment)
    {
        var value = transponder.Trim();

        if (!string.IsNullOrWhiteSpace(value))
        {
            var duplicate = SessionRacers.FirstOrDefault(r =>
                r.CarNumber != racer.CarNumber &&
                SplitTransponders(r.TransponderId).Any(t =>
                    string.Equals(t, value, StringComparison.OrdinalIgnoreCase)));

            if (duplicate is not null)
            {
                MessageBox.Show(this,
                    $"{value} is already assigned to {duplicate.DisplayName} in the current race.",
                    "Duplicate transponder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
        }

        racer.TransponderId = value;

        var entrant = _loadedSession?.Entrants.FirstOrDefault(e =>
            string.Equals(e.DriverId, racer.DriverId, StringComparison.OrdinalIgnoreCase));

        if (entrant is not null)
            entrant.TransponderId = value;

        EventDriverAssignment? eventAssignment = null;

        if (_activeEvent is not null)
        {
            eventAssignment = _activeEvent.DriverAssignments.FirstOrDefault(a =>
                string.Equals(a.DriverId, racer.DriverId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.ClassId, racer.ClassId, StringComparison.OrdinalIgnoreCase));

            if (eventAssignment is null)
            {
                eventAssignment = new EventDriverAssignment
                {
                    DriverId = racer.DriverId,
                    ClassId = racer.ClassId,
                    TransponderId = value,
                    SeedRank = 0
                };
                _activeEvent.DriverAssignments.Add(eventAssignment);
            }
            else
            {
                eventAssignment.TransponderId = value;
            }

            UpdateFutureSessionTransponder(eventAssignment);
        }

        if (persistPermanentAssignment)
        {
            var permanent = _driverAssignments.FirstOrDefault(a =>
                string.Equals(a.DriverId, racer.DriverId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.ClassId, racer.ClassId, StringComparison.OrdinalIgnoreCase));

            if (permanent is null)
            {
                permanent = new DriverClassAssignment
                {
                    DriverId = racer.DriverId,
                    ClassId = racer.ClassId,
                    TransponderId = value,
                    SeedRank = 0
                };
                _driverAssignments.Add(permanent);
            }
            else
            {
                permanent.TransponderId = value;
            }
        }

        EventDriverChecklist?.Items.Refresh();
        AssignmentGrid?.Items.Refresh();
        await CaptureAndSaveSettingsAsync();
        return true;
    }

    private void RenumberLiveRaceRoster()
    {
        for (var i = 0; i < SessionRacers.Count; i++)
            SessionRacers[i].CarNumber = i + 1;

        if (_loadedSession is not null)
        {
            for (var i = 0; i < _loadedSession.Entrants.Count; i++)
                _loadedSession.Entrants[i].CarNumber = i + 1;

            PrepareCheckInForSession(_loadedSession.Id, restart: true);
        }

        var cls = RaceClassCombo.SelectedItem as ClassDefinition
                  ?? Classes.FirstOrDefault(c => c.Id == _loadedSession?.ClassId);

        if (cls is not null)
            ShowLoadedRosterStandings(cls);

        ResetDriverLapPanelForSession();
        UpdateSessionRosterText();
    }
}
