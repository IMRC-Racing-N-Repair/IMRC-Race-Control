using System.IO;
using System.Windows;
using System.Windows.Controls;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private bool _restartRaceInProgress;

    private void CurrentRaceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents || _updatingDashboardSelectors || !IsLoaded)
            return;

        if (CurrentRaceCombo.SelectedItem is not EventSessionDefinition selected)
            return;

        SelectDashboardRace(selected);
        ReturnFocusToRaceDashboard();
    }

    private void SelectDashboardRace(EventSessionDefinition selected)
    {
        if (_restartRaceInProgress || _activeEvent is null || _activeEvent.Status != EventStatus.Active)
        {
            RestoreDashboardRaceSelection();
            return;
        }

        if (_resultAutoSaveInProgress)
        {
            FooterStatusText.Text = "Wait for the completed result to finish saving before changing heats.";
            RestoreDashboardRaceSelection();
            return;
        }

        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            MessageBox.Show(this,
                "The current race is already running. End it, false-start it, or use RESTART RACE before changing to another heat.",
                "Race in progress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            RestoreDashboardRaceSelection();
            return;
        }

        if (_loadedSession is not null &&
            string.Equals(_loadedSession.Id, selected.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var previous = _loadedSession;

        // Moving to another heat is a temporary skip only. A loaded-but-unrun
        // heat returns to PLANNED so the operator can select it again later.
        if (previous is not null &&
            previous.ProgressStatus == EventSessionStatus.Loaded &&
            !string.Equals(previous.Id, selected.Id, StringComparison.OrdinalIgnoreCase))
        {
            previous.ProgressStatus = EventSessionStatus.Planned;
            previous.StartedAt = null;
            previous.CompletedAt = null;
        }

        LoadSessionIntoRaceControl(selected);

        if (selected.ProgressStatus == EventSessionStatus.Completed)
        {
            FooterStatusText.Text =
                $"Recalled completed race '{selected.Name}'. Select another heat to continue, or use RESTART RACE to rerun it.";
        }
        else if (previous is not null)
        {
            FooterStatusText.Text =
                $"Loaded '{selected.Name}'. '{previous.Name}' remains in the schedule and can be selected again later.";
        }
        else
        {
            FooterStatusText.Text = $"Loaded '{selected.Name}'.";
        }
    }

    private void RestoreDashboardRaceSelection() => RefreshRaceDayQueue();

    private async void RestartRaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_restartRaceInProgress)
            return;

        if (_resultAutoSaveInProgress)
        {
            FooterStatusText.Text = "The completed result is still being saved. Please wait before restarting.";
            return;
        }

        if (_activeEvent is null || _activeEvent.Status != EventStatus.Active)
        {
            MessageBox.Show(this,
                "Only a race in an active event can be restarted.",
                "Restart race",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_loadedSession is null)
        {
            MessageBox.Show(this,
                "Select a race/heat from the Current Race dropdown first.",
                "Restart race",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var session = _loadedSession;
        var wasCompleted = session.ProgressStatus == EventSessionStatus.Completed;
        var hasRecordedData =
            wasCompleted ||
            session.Results.Count > 0 ||
            session.Laps.Count > 0 ||
            session.Breakouts.Count > 0 ||
            _raceEngine.Laps.Count > 0 ||
            _raceEngine.Breakouts.Count > 0 ||
            _raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired or RaceState.Complete;

        if (hasRecordedData)
        {
            var message = wasCompleted
                ? $"Restart '{session.Name}'?\n\n" +
                  "This clears this heat's stored result and laps so it can be run again. " +
                  "Other heats and previously exported result files are not changed."
                : $"Restart '{session.Name}'?\n\n" +
                  "All timing and laps currently recorded for this heat will be cleared and the heat will return to READY.";

            if (MessageBox.Show(
                    this,
                    message,
                    "Restart race",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }
        }

        _restartRaceInProgress = true;
        RestartRaceButton.IsEnabled = false;
        CurrentRaceCombo.IsEnabled = false;
        DashboardRoundCombo.IsEnabled = false;

        try
        {
            // Stop every live timing source before touching the persistent heat.
            _raceTimer.Stop();
            _lapSpeech.Stop();
            _raceEngine.Reset();
            _recallMode = false;

            // Clear only this selected heat. Nothing else in the event queue is
            // marked skipped, removed, or rebuilt.
            session.ProgressStatus = EventSessionStatus.Loaded;
            session.StartedAt = null;
            session.CompletedAt = null;
            session.Results.Clear();
            session.Laps.Clear();
            session.Breakouts.Clear();

            RecentLaps.Clear();
            DriverLapRows.Clear();
            Standings.Clear();
            ClearDriverPassingHighlight();

            _resultAutoSaved = false;
            _resultAutoSaveInProgress = false;
            _endTonePlayed = false;
            _lastRenderedRaceState = RaceState.Ready;

            // If a completed qualifying result was restarted, remove only that
            // result from the live event ranking before the rerun.
            RecalculateEventRankings();
            RefreshEventRankings();

            PrepareCheckInForSession(session.Id, restart: true);

            // Reload the roster without starting a second asynchronous settings
            // write. v0.8.4 could launch two settings saves here at the same time,
            // which could throw an unhandled file-sharing exception and close WPF.
            LoadSessionIntoRaceControl(session, saveSettings: false);
            RefreshPlannedSessions(session.Id);
            RefreshRaceDayQueue();

            // One serialized settings write only.
            await CaptureAndSaveSettingsAsync();

            FooterStatusText.Text =
                $"{session.Name} restarted. The heat is READY with all timing/laps cleared.";
        }
        catch (Exception ex)
        {
            var logPath = WriteRestartErrorLog(ex);
            MessageBox.Show(this,
                "Race Control caught an error while restarting the heat instead of closing the application.\n\n" +
                ex.Message +
                (string.IsNullOrWhiteSpace(logPath) ? string.Empty : $"\n\nError log: {logPath}"),
                "Restart race error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            FooterStatusText.Text = "Restart failed safely. The application is still running.";
        }
        finally
        {
            _restartRaceInProgress = false;
            RefreshRaceDayQueue();
            ReturnFocusToRaceDashboard();
        }
    }

    private static string WriteRestartErrorLog(Exception ex)
    {
        try
        {
            var folder = Path.Combine(IMRCRaceControl.Services.ApplicationDataPaths.DataDirectory,
                "Logs");
            Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, $"restart_error_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path,
                $"IMRC Race Control restart error\n{DateTimeOffset.Now:O}\n\n{ex}");
            return path;
        }
        catch
        {
            return string.Empty;
        }
    }
}
