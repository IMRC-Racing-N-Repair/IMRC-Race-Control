using System.Windows;
using System.Windows.Controls;
using IMRCRaceControl.Core;
using IMRCRaceControl.Dialogs;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private readonly ToneService _toneService = new();
    private bool _endTonePlayed;

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        _lapSpeech.Stop();
        var dialog = new SettingsDialog(_settings) { Owner = this, OpenWeb = () => LocalBoard_Click(this, new RoutedEventArgs()) };
        if (dialog.ShowDialog() != true)
            return;

        dialog.ApplyRaceAudio(_settings);
        ApplyScreenZoom();
        _settings.WebShowOverallPosition = dialog.WebShowOverallPosition;
        _settings.SpectatorVisibleColumns = dialog.SpectatorVisibleColumns;
        _settings.SpectatorShowHeader = dialog.SpectatorShowHeader;
        _settings.SpectatorShowClock = dialog.SpectatorShowClock;
        _settings.SpectatorShowNextUp = dialog.SpectatorShowNextUp;
        _settings.LapAnnouncementsEnabled = dialog.LapAnnouncementsEnabled;
        _settings.PracticeLapAnnouncementsEnabled = dialog.PracticeLapAnnouncementsEnabled;
        _settings.LapSpeechVoice = dialog.LapSpeechVoice;
        _settings.LapAnnouncementsTenthsOnly = dialog.LapAnnouncementsTenthsOnly;
        _settings.AnnounceCheckInNames = dialog.AnnounceCheckInNames;
        _settings.LapSpeechRate = dialog.LapSpeechRate;
        _settings.NameSpeechRate = dialog.NameSpeechRate;
        _settings.LapSpeechBusyMode = dialog.LapSpeechBusyMode;
        _settings.LapSpeechMaxAgeMilliseconds = dialog.LapSpeechMaxAgeMilliseconds;
        _settings.LapSpeechVolume = dialog.LapSpeechVolume;
        _lapSpeech.ResetFailure();
        ApplyLapSpeechControls();
        _settings.DriverCrossingStyle = dialog.DriverCrossingStyle;
        _settings.DriverCrossingColor = dialog.DriverCrossingColor;
        _settings.FinishedDriverStyle = dialog.FinishedDriverStyle;
        _settings.FinishedDriverColor = dialog.FinishedDriverColor;
        _settings.Theme = dialog.SelectedTheme;
        _settings.StartTone = dialog.StartTone;
        _settings.StartToneLength = dialog.StartLength;
        _settings.StartToneFile = dialog.StartToneFile;
        _settings.MinimumLapTone = dialog.MinimumTone;
        _settings.MinimumLapToneLength = dialog.MinimumLength;
        _settings.MinimumLapToneFile = dialog.MinimumToneFile;
        _settings.EndTone = dialog.EndTone;
        _settings.EndToneLength = dialog.EndLength;
        _settings.EndToneFile = dialog.EndToneFile;
        _settings.PassingTone = dialog.PassingTone;
        _settings.PassingToneLength = dialog.PassingLength;
        _settings.PassingToneFile = dialog.PassingToneFile;

        _settings.DashboardVisibleColumns = dialog.VisibleDashboardColumns.ToList();
        _settings.PracticeVisibleStats = dialog.VisiblePracticeStats.ToList();
        _settings.PracticeCardsPerRow = dialog.PracticeCardsPerRow;
        _settings.DashboardBackgroundColor = dialog.DashboardBackgroundColor;
        _settings.DashboardPanelColor = dialog.DashboardPanelColor;
        _settings.DashboardRowColor = dialog.DashboardRowColor;
        _settings.DashboardAlternateRowsEnabled = dialog.DashboardAlternateRowsEnabled;
        _settings.DashboardAlternateRowColor = dialog.DashboardAlternateRowColor;
        _settings.DashboardTextColor = dialog.DashboardTextColor;
        _settings.DashboardPassingHighlightColor = dialog.DashboardHighlightColor;
        _settings.DashboardFastestLapHighlightColor = dialog.DashboardFastestLapHighlightColor;
        _settings.DashboardNewBestLapHighlightColor = dialog.DashboardNewBestLapHighlightColor;
        _settings.DashboardCheckInGlowColor = dialog.DashboardCheckInGlowColor;
        _settings.DashboardMinimumLapGlowColor = dialog.DashboardMinimumLapGlowColor;
        _settings.DashboardMinimumLapGlowSeconds = dialog.DashboardMinimumLapGlowSeconds;
        _settings.DashboardPositionUpColor = dialog.DashboardPositionUpColor;
        _settings.DashboardPositionDownColor = dialog.DashboardPositionDownColor;
        _settings.DashboardPositionSameColor = dialog.DashboardPositionSameColor;
        _settings.DashboardColorsFollowTheme = dialog.DashboardColorsFollowTheme;

        ThemeService.Apply(_settings.Theme);
        ApplyDashboardPreferences();
        ApplyPracticeStatPreferences();
        ApplyPracticeLayoutPreferences();
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"Settings saved • {_settings.Theme.ToDisplay()} • dashboard and race audio updated.";
    }

    private async void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != MainTabs || !IsLoaded)
            return;

        if (MainTabs.SelectedItem == RaceTab)
            EnsureCurrentSessionLoadedForDashboard();
        else if (MainTabs.SelectedItem == PracticeTab && PracticeViewTabs.SelectedIndex == 1)
            await RefreshPracticeHistoryAsync();
        else if (MainTabs.SelectedItem == DriversTab)
            RefreshDriverRatings();
        else if (MainTabs.SelectedItem == SeriesTab)
        {
            RefreshSeriesEventChoices();
            RefreshSeriesClassChoices();
            RecalculateSeriesStandings();
        }
    }

    private void EnsureCurrentSessionLoadedForDashboard()
    {
        if (_activeEvent is null ||
            _activeEvent.Status != EventStatus.Active ||
            _resultAutoSaveInProgress ||
            _raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
            return;

        // v0.9.3: once the race director has selected a heat, keep it on the
        // dashboard even after it completes. Moving forward/backward is now
        // explicit through Previous Race / Next Race (or the dropdown).
        if (_loadedSession is not null &&
            _activeEvent.Sessions.Any(s =>
                string.Equals(s.Id, _loadedSession.Id, StringComparison.OrdinalIgnoreCase)))
        {
            RefreshRaceDayQueue();
            return;
        }

        var current = GetQueueSessions()
            .Where(s => s.ProgressStatus is not EventSessionStatus.Completed and not EventSessionStatus.Skipped)
            .OrderBy(s => s.QueueOrder)
            .FirstOrDefault(s => s.ProgressStatus == EventSessionStatus.Loaded)
            ?? GetQueueSessions()
                .Where(s => s.ProgressStatus == EventSessionStatus.Planned)
                .OrderBy(s => s.QueueOrder)
                .FirstOrDefault();

        if (current is not null)
            LoadSessionIntoRaceControl(current);
    }

    private void ShowLoadedRosterStandings(ClassDefinition cls)
    {
        Standings.Clear();
        var clock = (_loadedSession?.RaceFormat ?? cls.RaceFormat) == RaceFormat.Laps
            ? "0:00.0"
            : RaceEngine.FormatClock(TimeSpan.FromMinutes(_loadedSession?.RaceMinutesOverride ?? cls.MinutesFor(_loadedSession?.SessionType ?? SessionType.Qualifying)));

        var ordered = SessionRacers
            .Where(r => r.Enabled)
            .OrderBy(r => r.CarNumber)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var racer = ordered[i];
            Standings.Add(new StandingRow
            {
                Position = i + 1,
                CarNumber = racer.CarNumber,
                DriverName = racer.DisplayName,
                Laps = 0,
                Clock = clock,
                LastLap = "—",
                BestLap = "—",
                BestLapNumber = "—",
                Top2 = "—",
                Top3 = "—",
                Top5 = "—",
                AverageLap = "—",
                Average5 = "—",
                Average10 = "—",
                Average15 = "—",
                Consistency = "—",
                Pace = "—",
                GapToLeader = "—",
                GapBehind = "—",
                TotalTime = "—",
                Status = "READY",
                ManualActionText = "CHECK IN",
                IsCheckedIn = _checkedInCars.Contains(racer.CarNumber),
                IsFastestLap = false
            });
        }

        RaceStateText.Text = "READY";
        RaceClockText.Text = clock;
        FastestSessionText.Text = "Fastest: —";
        LastPassingText.Text = "Waiting for a passing…";
        HeaderRaceStateText.Text = "READY";
        _lastRenderedRaceState = RaceState.Ready;
    }

    private void EventRosterClassCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents ||
            EventRosterClassCombo.SelectedItem is not EventClassRow selected ||
            _activeEvent is null)
            return;

        _eventRosterClassId = selected.ClassId;

        _suppressUiEvents = true;
        try
        {
            EventClassChecklist.SelectedItem =
                EventClassChoices.FirstOrDefault(c => c.ClassId == selected.ClassId);
        }
        finally
        {
            _suppressUiEvents = false;
        }

        RefreshEventDriverChoices();
    }

    private async void EventDriverTransponderInline_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_activeEvent?.Status != EventStatus.Active ||
            sender is not TextBox box ||
            box.Tag is not string driverId ||
            string.IsNullOrWhiteSpace(_eventRosterClassId))
            return;

        var assignment = _activeEvent.DriverAssignments.FirstOrDefault(a =>
            a.ClassId == _eventRosterClassId &&
            a.DriverId == driverId);

        if (assignment is null)
            return;

        assignment.TransponderId = box.Text.Trim();
        UpdateFutureSessionTransponder(assignment);

        var choice = EventDriverChoices.FirstOrDefault(c => c.DriverId == driverId);
        if (choice is not null)
            choice.TransponderId = assignment.TransponderId;

        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = string.IsNullOrWhiteSpace(assignment.TransponderId)
            ? "Event driver will be manually scored unless a transponder is added."
            : $"Event transponder saved: {assignment.TransponderId}.";
    }

    private async void OpenRoundMainBuilder_Click(object sender, RoutedEventArgs e) =>
        await OpenRoundMainBuilderAsync(null);

    private async void EditSelectedRound_Click(object sender, RoutedEventArgs e)
    {
        if (PlannedSessionGrid.SelectedItem is not EventSessionDefinition selected)
        {
            MessageBox.Show(this, "Select a saved heat or main from the event list first.", "Edit round",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await OpenRoundMainBuilderAsync(selected.Id);
    }

    private async Task OpenRoundMainBuilderAsync(string? editSessionId)
    {
        if (_activeEvent is null)
        {
            MessageBox.Show(this, "Create or select an event first.", "Rounds / mains",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_activeEvent.Status != EventStatus.Active)
        {
            MessageBox.Show(this, "Completed events are read-only.", "Rounds / mains",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            MessageBox.Show(this, "Finish the live race before rebuilding rounds or mains.", "Race in progress",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_activeEvent.ClassIds.Count == 0)
        {
            MessageBox.Show(this, "Check at least one class into the event first.", "Rounds / mains",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_resultAutoSaveInProgress)
        {
            FooterStatusText.Text = "Finishing the race result. Please wait before editing rounds.";
            return;
        }

        RecalculateEventRankings();

        var dialog = new RoundMainBuilderDialog(
            _activeEvent,
            Classes.ToList(),
            Drivers.ToList(),
            _eventRosterClassId, editSessionId, _settings)
        {
            Owner = this
        };

        dialog.SaveRequested = () => ApplyRoundBuilderChangesAsync(dialog);
        dialog.SaveBuilderRequested = builder => ApplyRoundBuilderChangesAsync(builder);
        var dialogResult = dialog.ShowDialog();

        var deletedIds = dialog.DeletedSessionIds
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var deletedLoadedSession =
            _loadedSession is not null &&
            deletedIds.Contains(_loadedSession.Id);

        if (deletedLoadedSession)
        {
            _loadedSession = null;
            _recallMode = false;
            _lapSpeech.Stop();
            _raceEngine.Reset();
            SessionRacers.Clear();
            Standings.Clear();
            RecentLaps.Clear();
            UnknownTransponderHits.Clear();
            ResetDriverLapPanelForSession();
            UpdateSessionRosterText();
        }

        // DELETE SELECTED in the builder is an explicit immediate event edit.
        // Persist it even when the operator closes/cancels without rebuilding
        // the preview above.
        if (dialogResult != true)
        {
            if (dialog.HasDirectEventChanges)
            {
                RecalculateEventRankings();
                RebuildRemainingQueueFromClassOrder();
                RefreshPlannedSessions();
                RefreshRaceDayFlow();
                await CaptureAndSaveSettingsAsync();

                FooterStatusText.Text =
                    $"{deletedIds.Count} saved race{(deletedIds.Count == 1 ? string.Empty : "s")} deleted from the event.";

                if (MainTabs.SelectedItem == RaceTab)
                    EnsureCurrentSessionLoadedForDashboard();
            }

            return;
        }

        await ApplyRoundBuilderChangesAsync(dialog);
    }

    private async Task<bool> ApplyRoundBuilderChangesAsync(RoundMainBuilderDialog dialog)
    {
        if (_activeEvent is null) return false;
        var deletedIds = dialog.DeletedSessionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_loadedSession is not null && deletedIds.Contains(_loadedSession.Id))
        {
            _loadedSession = null;
            _recallMode = false;
            _lapSpeech.Stop();
            _raceEngine.Reset();
            SessionRacers.Clear(); Standings.Clear(); RecentLaps.Clear(); UnknownTransponderHits.Clear();
            ResetDriverLapPanelForSession(); UpdateSessionRosterText();
        }
        var targetIds = dialog.ReplaceClassIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = _activeEvent.Sessions
            .Where(s => targetIds.Contains(s.ClassId) &&
                        ((dialog.BuiltSessionType == SessionType.Qualifying &&
                          s.SessionType == SessionType.Qualifying &&
                          s.Round == dialog.BuiltRound) ||
                         (dialog.BuiltSessionType == SessionType.Race &&
                          s.SessionType == SessionType.Race)))
            .ToList();

        if (!SavedRoundEditor.PreservesCompletedResults(existing, dialog.BuiltSessions))
        {
            // Direct deletes may already have happened in this dialog; preserve
            // those even if the preview cannot overwrite remaining completed
            // history.
            if (dialog.HasDirectEventChanges)
            {
                RecalculateEventRankings();
                RebuildRemainingQueueFromClassOrder();
                RefreshPlannedSessions();
                RefreshRaceDayFlow();
                await CaptureAndSaveSettingsAsync();
            }

            MessageBox.Show(this,
                "That preview would replace a scope that still contains completed results. Delete the completed race(s) from the bottom list first, or leave that history unchanged.",
                "Completed results remain",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            if (MainTabs.SelectedItem == RaceTab)
                EnsureCurrentSessionLoadedForDashboard();

            return false;
        }

        var affectedIds = existing.Where(s => s.ProgressStatus != EventSessionStatus.Completed)
            .Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var loadedId = _loadedSession?.Id;
        SavedRoundEditor.ReplaceScope(_activeEvent, existing, dialog.BuiltSessions);
        foreach (var driver in dialog.AddedDrivers)
            if (!Drivers.Any(d => d.Id == driver.Id)) Drivers.Add(driver);
        // Car numbers can change after lineup edits. Old car-number check-ins
        // must not transfer to another driver.
        if (affectedIds.Contains(_checkInSessionId))
        {
            _raceCheckIn.Clear();
        }
        if (loadedId is not null && affectedIds.Contains(loadedId))
        {
            var updated = _activeEvent.Sessions.FirstOrDefault(s => s.Id == loadedId);
            if (updated is not null) LoadSessionIntoRaceControl(updated, saveSettings: false);
            else
            {
                _loadedSession = null;
                _recallMode = false;
                _lapSpeech.Stop();
                _raceEngine.Reset();
                SessionRacers.Clear();
                Standings.Clear();
                RecentLaps.Clear();
                UnknownTransponderHits.Clear();
                ResetDriverLapPanelForSession();
                UpdateSessionRosterText();
            }
        }

        RecalculateEventRankings();
        RebuildRemainingQueueFromClassOrder();
        RefreshPlannedSessions();
        RefreshRaceDayFlow();
        await CaptureAndSaveSettingsAsync();

        if (dialog.BuiltSessionType == SessionType.Qualifying)
        {
            var nextRound = _activeEvent.Sessions
                .Where(s => s.SessionType == SessionType.Qualifying)
                .Select(s => s.Round)
                .DefaultIfEmpty(dialog.BuiltRound)
                .Max() + 1;

            FooterStatusText.Text =
                $"Q{dialog.BuiltRound} saved. The next new qualifying round will be Q{nextRound}.";
        }
        else
        {
            FooterStatusText.Text =
                "Mains saved/rebuilt. Lower mains remain ordered before A Main.";
        }

        if (dialog.HasDirectEventChanges)
        {
            FooterStatusText.Text +=
                $" {deletedIds.Count} previously saved race{(deletedIds.Count == 1 ? string.Empty : "s")} also deleted.";
        }

        if (MainTabs.SelectedItem == RaceTab)
            EnsureCurrentSessionLoadedForDashboard();
        return true;

    }

}
