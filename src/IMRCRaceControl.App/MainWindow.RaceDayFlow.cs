using static IMRCRaceControl.Core.MainResultCalculator;
using System.IO;
using System.Windows;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private sealed record RoundPerformance(
        int Round,
        int Position,
        int Laps,
        double? ElapsedSeconds,
        double? BestLapSeconds);

    private sealed record DriverRoundAggregate(
        string ClassId,
        string ClassName,
        string DriverId,
        string DriverName,
        List<RoundPerformance> Rounds);

    private void RefreshRaceDayFlow()
    {
        RefreshEventRankings();
        RefreshRaceDayQueue();
        EventSelectCombo?.Items.Refresh();
        PlannedSessionGrid?.Items.Refresh();
        UpdateEventEditorAvailability();
    }

    private void RebuildSessionQueueOrder()
    {
        if (_activeEvent is null)
            return;

        var sessions = _activeEvent.Sessions.ToList();
        if (sessions.Count == 0)
            return;

        var classOrder = _activeEvent.ClassIds
            .Select((id, index) => new { id, index })
            .ToDictionary(x => x.id, x => x.index, StringComparer.OrdinalIgnoreCase);

        var currentOrders = sessions.Select(s => s.QueueOrder).ToList();
        var validExistingOrder =
            currentOrders.All(q => q > 0) &&
            currentOrders.Distinct().Count() == sessions.Count;

        if (validExistingOrder)
        {
            // Preserve qualifying/manual queue positions, but always normalize
            // the remaining mains inside their existing queue slots so lower
            // mains run first and every A Main remains last.
            var remainingMains = sessions
                .Where(s =>
                    s.SessionType == SessionType.Race &&
                    s.ProgressStatus is not EventSessionStatus.Completed and
                    not EventSessionStatus.Skipped)
                .ToList();

            if (remainingMains.Count > 1)
            {
                var queueSlots = remainingMains
                    .Select(s => s.QueueOrder)
                    .OrderBy(q => q)
                    .ToList();

                var autoSortedMains = remainingMains
                    .OrderByDescending(s => s.Heat)
                    .ThenBy(s => classOrder.TryGetValue(s.ClassId, out var index) ? index : int.MaxValue)
                    .ThenBy(s => s.MainLeg)
                    .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (var i = 0; i < autoSortedMains.Count; i++)
                    autoSortedMains[i].QueueOrder = queueSlots[i];
            }

            var order = 1;
            foreach (var session in sessions.OrderBy(s => s.QueueOrder))
                session.QueueOrder = order++;

            RaceSchedule.AssignRaceNumbers(sessions);
            return;
        }

        var ordered = sessions
            .OrderBy(s => s.SessionType == SessionType.Qualifying ? 0 : s.SessionType == SessionType.Race ? 1 : 2)
            .ThenBy(s => s.SessionType == SessionType.Race ? -s.Heat : s.Round)
            .ThenBy(s => classOrder.TryGetValue(s.ClassId, out var index) ? index : int.MaxValue)
            .ThenBy(s => s.SessionType == SessionType.Race ? s.MainLeg : s.Heat)
            .ThenBy(s => s.Name)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
            ordered[i].QueueOrder = i + 1;
        RaceSchedule.AssignRaceNumbers(sessions);
    }

    private IReadOnlyList<EventSessionDefinition> GetQueueSessions()
    {
        if (_activeEvent is null)
            return [];

        return _activeEvent.Sessions
            .OrderBy(s => s.QueueOrder <= 0 ? int.MaxValue : s.QueueOrder)
            .ThenBy(s => s.Round)
            .ThenBy(s => s.ClassName)
            .ThenBy(s => s.Heat)
            .ThenBy(s => s.MainLeg)
            .ToList();
    }

    private void RefreshRaceDayQueue()
    {
        if (QueueCurrentText is null)
            return;

        if (_activeEvent is null)
        {
            QueueCurrentText.Text = "No event";
            QueueCurrentStatusText.Text = "Create a race-day event first";
            QueueNextText.Text = "—";
            QueueNextStatusText.Text = "—";
            QueueProgressText.Text = "0 complete • 0 remaining";
            QueueEventStatusText.Text = "No active event";

            RefreshDashboardRaceSelectors([], null, false);

            ReplaceRoundProjectionRows([]);
            if (RoundProjectionInfoText is not null)
                RoundProjectionInfoText.Text = "Load a qualifying heat.";

            RestartRaceButton.IsEnabled = false;
            PreviousRaceButton.IsEnabled = false;
            NextRaceButton.IsEnabled = false;
            LoadCurrentQueueButton.IsEnabled = false;
            LoadNextQueueButton.IsEnabled = false;
            return;
        }

        var sessions = GetQueueSessions();

        RaceSchedule.AssignRaceNumbers(sessions);

        var remaining = sessions
            .Where(s => s.ProgressStatus is not EventSessionStatus.Completed and not EventSessionStatus.Skipped)
            .ToList();

        var queueCurrent = remaining.FirstOrDefault(s =>
                               s.ProgressStatus is EventSessionStatus.Loaded or EventSessionStatus.Running)
                           ?? remaining.FirstOrDefault();

        // When the operator deliberately selected another heat—or recalled a
        // completed heat—show that actual selection rather than forcing the
        // lowest queue number into the Current Race display.
        var displayedCurrent = _loadedSession is not null &&
                               sessions.Any(s => string.Equals(s.Id, _loadedSession.Id, StringComparison.OrdinalIgnoreCase))
            ? _loadedSession
            : queueCurrent;

        var next = queueCurrent is null
            ? null
            : remaining
                .Where(s => !string.Equals(s.Id, queueCurrent.Id, StringComparison.OrdinalIgnoreCase) &&
                            s.QueueOrder > queueCurrent.QueueOrder)
                .OrderBy(s => s.QueueOrder)
                .FirstOrDefault()
              ?? remaining.FirstOrDefault(s =>
                    queueCurrent is not null &&
                    !string.Equals(s.Id, queueCurrent.Id, StringComparison.OrdinalIgnoreCase));

        var completed = sessions.Count(s => s.ProgressStatus == EventSessionStatus.Completed);
        var skipped = sessions.Count(s => s.ProgressStatus == EventSessionStatus.Skipped);
        var lastCompleted = sessions
            .Where(s => s.ProgressStatus == EventSessionStatus.Completed)
            .OrderByDescending(s => s.CompletedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        QueueCurrentText.Text = displayedCurrent?.Name ??
                                (sessions.Count == 0 ? "No races built yet" : "Event queue complete");
        QueueCurrentStatusText.Text = displayedCurrent is null
            ? (sessions.Count == 0 ? "Build rounds or mains in Event Setup." : "No race selected.")
            : DescribeQueueSession(displayedCurrent);

        QueueNextText.Text = next?.Name ?? "—";
        QueueNextStatusText.Text = next is null
            ? "—"
            : DescribeQueueSession(next);

        QueueProgressText.Text = skipped > 0
            ? $"{completed} complete • {skipped} skipped • {remaining.Count} remaining"
            : $"{completed} complete • {remaining.Count} remaining";

        QueueEventStatusText.Text = lastCompleted is null
            ? $"{_activeEvent.StatusDisplay} • {_activeEvent.EventDate:MMM d, yyyy} • Last complete: —"
            : $"{_activeEvent.StatusDisplay} • Last complete: {lastCompleted.Name}";

        var active = _activeEvent.Status == EventStatus.Active;
        var raceSwitchAllowed = !_resultAutoSaveInProgress && !_restartRaceInProgress &&
                                _raceEngine.State is not RaceState.Armed and
                                not RaceState.Running and
                                not RaceState.TimeExpired;

        RefreshDashboardRaceSelectors(sessions, displayedCurrent, active && raceSwitchAllowed);

        RestartRaceButton.IsEnabled =
            active &&
            _loadedSession is not null &&
            !_resultAutoSaveInProgress;

        var visibleSessions = sessions
            .Where(s => s.ProgressStatus != EventSessionStatus.Skipped)
            .OrderBy(s => s.QueueOrder)
            .ToList();

        var selectedIndex = _loadedSession is null
            ? -1
            : visibleSessions.FindIndex(s =>
                string.Equals(s.Id, _loadedSession.Id, StringComparison.OrdinalIgnoreCase));

        PreviousRaceButton.IsEnabled =
            active && raceSwitchAllowed && selectedIndex > 0;

        NextRaceButton.IsEnabled =
            active && raceSwitchAllowed &&
            selectedIndex >= 0 &&
            selectedIndex < visibleSessions.Count - 1;

        LoadCurrentQueueButton.IsEnabled = active && queueCurrent is not null && raceSwitchAllowed;
        LoadNextQueueButton.IsEnabled = active && next is not null && raceSwitchAllowed;

        RefreshRoundProjection();
    }

    private static string DescribeQueueSession(EventSessionDefinition session)
    {
        return session.SessionType == SessionType.Qualifying
            ? $"{session.ClassName} • Q{session.Round} • Heat {session.Heat}"
            : $"{session.ClassName} • {session.Name}";
    }

    private void LoadCurrentQueue_Click(object sender, RoutedEventArgs e)
    {
        var remaining = GetQueueSessions()
            .Where(s => s.ProgressStatus is not EventSessionStatus.Completed and not EventSessionStatus.Skipped)
            .ToList();

        var session = remaining.FirstOrDefault(s =>
                          s.ProgressStatus is EventSessionStatus.Loaded or EventSessionStatus.Running)
                      ?? remaining.FirstOrDefault();

        if (session is null)
        {
            FooterStatusText.Text = "There is no remaining race in the event queue.";
            return;
        }

        LoadSessionIntoRaceControl(session);
    }

    private void LoadNextQueue_Click(object sender, RoutedEventArgs e)
    {
        var remaining = GetQueueSessions()
            .Where(s => s.ProgressStatus is not EventSessionStatus.Completed and not EventSessionStatus.Skipped)
            .ToList();

        var current = remaining.FirstOrDefault(s =>
                          s.ProgressStatus is EventSessionStatus.Loaded or EventSessionStatus.Running)
                      ?? remaining.FirstOrDefault();

        var session = current is null
            ? null
            : remaining
                .Where(s => !string.Equals(s.Id, current.Id, StringComparison.OrdinalIgnoreCase) &&
                            s.QueueOrder > current.QueueOrder)
                .OrderBy(s => s.QueueOrder)
                .FirstOrDefault()
              ?? remaining.FirstOrDefault(s =>
                    current is not null &&
                    !string.Equals(s.Id, current.Id, StringComparison.OrdinalIgnoreCase));

        if (session is null)
        {
            FooterStatusText.Text = "There is no next race in the event queue.";
            return;
        }

        ReleaseFinishedSpectatorFrame();
        LoadSessionIntoRaceControl(session);
    }

    private void OpenEventSetup_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedItem = EventTab;
    }

    private async Task PersistCompletedSessionAsync(EventSessionDefinition? completedSession)
    {
        if (completedSession is null || _activeEvent is null)
            return;

        var now = DateTimeOffset.Now;
        completedSession.ProgressStatus = EventSessionStatus.Completed;
        completedSession.CompletedAt = now;
        completedSession.StartedAt ??= _raceEngine.StartedAt ?? now;
        completedSession.Results = _raceEngine.GetStandings(now).ToList();
        completedSession.Laps = _raceEngine.Laps.OrderBy(l => l.Sequence).ToList();
        completedSession.Breakouts = _raceEngine.Breakouts
            .OrderBy(b => b.Timestamp)
            .ThenBy(b => b.Sequence)
            .ToList();

        var bumpMessage = ApplyMainBumpUps(completedSession);

        StartRaceButton.IsEnabled = false;
        FalseStartButton.IsEnabled = false;
        StopRaceButton.IsEnabled = false;

        RecalculateEventRankings();
        RebuildSessionQueueOrder();
        RefreshPlannedSessions(completedSession.Id);
        RefreshEventRankings();
        RefreshRaceDayQueue();

        EventSelectCombo.Items.Refresh();
        PlannedSessionGrid.Items.Refresh();

        await CaptureAndSaveSettingsAsync();

        var next = GetQueueSessions()
            .FirstOrDefault(s => s.ProgressStatus is not EventSessionStatus.Completed and not EventSessionStatus.Skipped);

        var completionMessage = next is null
            ? $"{completedSession.Name} complete. All planned event sessions are finished."
            : $"{completedSession.Name} complete. Next: {next.Name}.";

        FooterStatusText.Text = string.IsNullOrWhiteSpace(bumpMessage)
            ? completionMessage
            : $"{completionMessage} {bumpMessage}";
    }

    private string ApplyMainBumpUps(EventSessionDefinition completedSession)
    {
        if (_activeEvent is null || completedSession.SessionType != SessionType.Race)
            return string.Empty;

        var series = _activeEvent.Sessions
            .Where(s =>
                s.SessionType == SessionType.Race &&
                string.Equals(s.ClassId, completedSession.ClassId, StringComparison.OrdinalIgnoreCase) &&
                s.Heat == completedSession.Heat)
            .OrderBy(s => s.MainLeg)
            .ThenBy(s => s.QueueOrder)
            .ToList();

        if (series.Count == 0)
            return string.Empty;

        var unfinished = series.Any(s =>
            s.ProgressStatus is not EventSessionStatus.Completed and not EventSessionStatus.Skipped);

        if (unfinished)
        {
            var completedCount = series.Count(s => s.ProgressStatus == EventSessionStatus.Completed);
            var plannedRuns = Math.Max(series.Count, completedSession.MainRunCount);
            return plannedRuns > 1
                ? $"{MainLabel(completedSession.Heat)} series: {completedCount}/{plannedRuns} runs complete."
                : string.Empty;
        }

        var standings = CalculateMainSeriesStandings(series);
        if (standings.Count == 0)
            return "No eligible main-series results were found.";

        var countBest = series
            .Where(s => s.ProgressStatus == EventSessionStatus.Completed)
            .Select(s => Math.Max(1, s.MainCountBest))
            .DefaultIfEmpty(1)
            .First();

        var completedLegs = series.Count(s => s.ProgressStatus == EventSessionStatus.Completed);
        countBest = Math.Min(countBest, Math.Max(1, completedLegs));

        var leader = standings[0];
        var seriesMessage = series.Count > 1
            ? $"{MainLabel(completedSession.Heat)} OVERALL: {leader.Entrant.DisplayName} P1 • {leader.Points} pts • best {countBest} of {completedLegs}."
            : $"{MainLabel(completedSession.Heat)} winner: {leader.Entrant.DisplayName}.";

        var bumpCount = series.Select(s => s.BumpUpCount).DefaultIfEmpty(0).Max();
        if (bumpCount <= 0)
            return seriesMessage;

        var targetSessions = _activeEvent.Sessions
            .Where(s =>
                s.SessionType == SessionType.Race &&
                string.Equals(s.ClassId, completedSession.ClassId, StringComparison.OrdinalIgnoreCase) &&
                s.Heat == completedSession.Heat - 1)
            .OrderBy(s => s.MainLeg)
            .ThenBy(s => s.QueueOrder)
            .ToList();

        if (targetSessions.Count == 0)
            return $"{seriesMessage} Bump-up target was not found.";

        if (targetSessions.Any(s =>
                s.ProgressStatus is EventSessionStatus.Completed or EventSessionStatus.Running))
        {
            return $"{seriesMessage} {bumpCount} bump-up spot(s) could not be applied because {MainLabel(completedSession.Heat - 1)} has already started.";
        }

        var finishers = standings
            .Take(bumpCount)
            .Select(s => s.Entrant)
            .ToList();

        var addedNames = new List<string>();

        foreach (var finisher in finishers)
        {
            var addedToAtLeastOneLeg = false;

            foreach (var target in targetSessions)
            {
                if (target.Entrants.Any(e =>
                        string.Equals(e.DriverId, finisher.DriverId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (target.MainCapacity > 0 && target.Entrants.Count >= target.MainCapacity)
                    continue;

                target.Entrants.Add(new SessionEntrant
                {
                    CarNumber = target.Entrants.Count + 1,
                    DriverId = finisher.DriverId,
                    DriverName = finisher.DriverName,
                    Nickname = finisher.Nickname,
                    TransponderId = finisher.TransponderId,
                    SeedRank = finisher.SeedRank
                });

                for (var i = 0; i < target.Entrants.Count; i++)
                    target.Entrants[i].CarNumber = i + 1;

                addedToAtLeastOneLeg = true;
            }

            if (addedToAtLeastOneLeg)
                addedNames.Add(finisher.DisplayName);
        }

        PlannedSessionGrid?.Items.Refresh();

        return addedNames.Count == 0
            ? $"{seriesMessage} Bump-up finishers were already present in {MainLabel(completedSession.Heat - 1)}."
            : $"{seriesMessage} BUMP UP → {MainLabel(completedSession.Heat - 1)}: {string.Join(", ", addedNames)}.";
    }

    private static string MainLabel(int mainOrder)
    {
        if (mainOrder is >= 1 and <= 26)
            return $"{(char)('A' + mainOrder - 1)} Main";

        return $"Main {mainOrder}";
    }

    private void LoadCompletedSessionForRecall(EventSessionDefinition session)
    {
        if (_activeEvent is null)
            return;

        var cls = Classes.FirstOrDefault(c => c.Id == session.ClassId);
        _loadedSession = session;
        _recallMode = true;
        _raceTimer.Stop();
        _lapSpeech.Stop();
        _raceEngine.Reset();

        SessionRacers.Clear();
        foreach (var entrant in session.Entrants.OrderBy(e => e.CarNumber))
        {
            SessionRacers.Add(new RacerEntry
            {
                CarNumber = entrant.CarNumber,
                Enabled = true,
                DriverId = entrant.DriverId,
                DriverName = entrant.DriverName,
                Nickname = entrant.Nickname,
                ClassId = session.ClassId,
                ClassName = session.ClassName,
                TransponderId = entrant.TransponderId
            });
        }

        _suppressUiEvents = true;
        try
        {
            if (cls is not null)
                RaceClassCombo.SelectedItem = cls;
            SelectComboByText(SessionTypeCombo, session.SessionType.ToString());
        }
        finally
        {
            _suppressUiEvents = false;
        }

        RaceEventText.Text = _activeEvent.Name;
        RaceSessionText.Text = session.Name;

        if (cls is not null)
        {
            RaceMinutesText.Text = (_loadedSession?.RaceMinutesOverride ?? cls.MinutesFor(_loadedSession?.SessionType ?? SessionType.Qualifying)).ToString("0.###");
            MinimumLapText.Text = cls.MinimumLapSeconds.ToString("0.###");
        }

        StartStyleText.Text = session.StartStyle.ToDisplay();
        UpdateSessionRosterText();

        Standings.Clear();
        _raceCheckIn.SelectSession(session.Id, restart: true);
        foreach (var result in session.Results.OrderBy(r => r.Position))
        {
            result.IsCheckedIn = true;
            result.ManualActionText = "PASS";
            _checkedInCars.Add(result.CarNumber);
            Standings.Add(result);
        }

        RecentLaps.Clear();
        foreach (var lap in session.Laps.OrderByDescending(l => l.Sequence).Take(250))
            RecentLaps.Add(lap);

        SetRaceSetupEnabled(false);
        StartRaceButton.IsEnabled = false;
        FalseStartButton.IsEnabled = false;
        StopRaceButton.IsEnabled = false;
        ResetRaceButton.IsEnabled = true;

        RenderRecalledRace();
        ResetDriverLapPanelForSession();
        RefreshRaceDayQueue();
        FooterStatusText.Text =
            $"Recalled {session.Name} from {_activeEvent.EventDate:MMM d, yyyy}. Use RESTART RACE if this heat needs to be run again.";
    }

    private void RenderRecalledRace()
    {
        if (!_recallMode || _loadedSession is null)
            return;

        var historicalEvent = _activeEvent?.Status == EventStatus.Completed;
        RaceStateText.Text = historicalEvent ? "ARCHIVED RESULT" : "COMPLETE";
        HeaderRaceStateText.Text = historicalEvent ? "RECALL" : "COMPLETE";
        RaceClockText.Text = "COMPLETE";
        HeaderMessageText.Text =
            $"{_activeEvent?.Name} • {_loadedSession.Name} • {_loadedSession.CompletedAt?.LocalDateTime:g}";

        var fastest = _loadedSession.Results
            .Where(s => s.IsFastestLap && s.BestLap != "—")
            .ToList();

        FastestSessionText.Text = fastest.Count == 0
            ? "Fastest: —"
            : "FASTEST: " + string.Join(" / ",
                fastest.Select(f => $"{f.DriverName} {f.BestLap} (L{f.BestLapNumber})"));

        LastPassingText.Text = _loadedSession.CompletedAt.HasValue
            ? $"Completed {_loadedSession.CompletedAt.Value.LocalDateTime:g} • {_loadedSession.Laps.Count} recorded laps"
            : $"{_loadedSession.Laps.Count} recorded laps";

        RefreshRoundProjection();
    }

    private async void EndEvent_Click(object sender, RoutedEventArgs e)
    {
        if (_resultAutoSaveInProgress)
        {
            MessageBox.Show(this, "The last race result is still being saved. End the event after that save finishes.", "End event",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_activeEvent is null || _activeEvent.Status == EventStatus.Completed)
            return;

        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            MessageBox.Show(this, "End the live race before ending the race-day event.", "End event",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var unfinished = _activeEvent.Sessions
            .Where(s => s.ProgressStatus is not EventSessionStatus.Completed and not EventSessionStatus.Skipped)
            .ToList();

        var message = unfinished.Count == 0
            ? $"End '{_activeEvent.Name}' and archive the event?"
            : $"'{_activeEvent.Name}' still has {unfinished.Count} unfinished session(s).\n\n" +
              "Ending the event will mark those sessions SKIPPED and preserve all completed results. Continue?";

        if (MessageBox.Show(this, message, "End race-day event",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        foreach (var session in unfinished)
            session.ProgressStatus = EventSessionStatus.Skipped;

        RecalculateEventRankings();

        _activeEvent.Status = EventStatus.Completed;
        _activeEvent.CompletedAt = DateTimeOffset.Now;

        try
        {
            _activeEvent.LastArchivePath = await _eventArchiveService.SaveAsync(_activeEvent);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"The event was completed, but the separate archive file could not be written:\n{ex.Message}",
                "Event archive", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        EventSelectCombo.Items.Refresh();
        RefreshPlannedSessions();
        RefreshEventRankings();
        UpdateRaceDashboardAvailability();
        RefreshRaceDayFlow();
        await CaptureAndSaveSettingsAsync();

        FooterStatusText.Text = string.IsNullOrWhiteSpace(_activeEvent.LastArchivePath)
            ? $"Event '{_activeEvent.Name}' completed and retained in Race Control."
            : $"Event '{_activeEvent.Name}' completed and archived: {Path.GetFileName(_activeEvent.LastArchivePath)}";
    }

    private async void ReopenEvent_Click(object sender, RoutedEventArgs e)
    {
        if (_activeEvent is null || _activeEvent.Status != EventStatus.Completed)
            return;

        if (MessageBox.Show(this,
                $"Reopen '{_activeEvent.Name}' for changes and additional racing?\n\n" +
                "The existing archive file remains untouched. A new archive will be written the next time the event is ended.",
                "Reopen event", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _activeEvent.Status = EventStatus.Active;
        _activeEvent.CompletedAt = null;

        foreach (var session in _activeEvent.Sessions.Where(s =>
                     s.ProgressStatus == EventSessionStatus.Skipped &&
                     s.Results.Count == 0 &&
                     s.Laps.Count == 0))
        {
            session.ProgressStatus = EventSessionStatus.Planned;
        }

        RebuildSessionQueueOrder();
        EventSelectCombo.Items.Refresh();
        UpdateRaceDashboardAvailability();
        RefreshRaceDayFlow();
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"Event '{_activeEvent.Name}' reopened.";
    }

    private void OpenEventArchives_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_eventArchiveService.ArchiveDirectory);
        OpenFolder(_eventArchiveService.ArchiveDirectory);
    }

    private List<string> GetCompletedRoundDriverOrder(string classId, int round)
    {
        if (_activeEvent is null)
            return [];

        var performances = new List<(string DriverId, string DriverName, int Laps, double? Elapsed, double? BestLap)>();

        foreach (var session in _activeEvent.Sessions.Where(s =>
                     s.SessionType == SessionType.Qualifying &&
                     s.ClassId == classId &&
                     s.Round == round &&
                     s.ProgressStatus == EventSessionStatus.Completed))
        {
            foreach (var result in session.Results)
            {
                var entrant = session.Entrants.FirstOrDefault(e => e.CarNumber == result.CarNumber);
                if (entrant is null || string.IsNullOrWhiteSpace(entrant.DriverId))
                    continue;

                var laps = session.Laps
                    .Where(l => l.CarNumber == result.CarNumber)
                    .OrderBy(l => l.LapNumber)
                    .ToList();

                performances.Add((
                    entrant.DriverId,
                    entrant.DisplayName,
                    laps.Count,
                    laps.Count == 0 ? null : laps[^1].Elapsed.TotalSeconds,
                    laps.Count == 0 ? null : laps.Min(l => l.LapTime.TotalSeconds)));
            }
        }

        return performances
            .GroupBy(p => p.DriverId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(x => x.Laps)
                .ThenBy(x => x.Elapsed ?? double.MaxValue)
                .ThenBy(x => x.BestLap ?? double.MaxValue)
                .First())
            .OrderByDescending(x => x.Laps)
            .ThenBy(x => x.Elapsed ?? double.MaxValue)
            .ThenBy(x => x.BestLap ?? double.MaxValue)
            .ThenBy(x => x.DriverName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.DriverId)
            .ToList();
    }

    private void RefreshEventRankings()
    {
        EventRankings.Clear();
        if (_activeEvent is null)
            return;

        if (_activeEvent.Rankings.Count == 0 &&
            _activeEvent.Sessions.Any(s => s.ProgressStatus == EventSessionStatus.Completed))
        {
            RecalculateEventRankings();
        }

        foreach (var row in _activeEvent.Rankings
                     .OrderBy(r => r.ClassName)
                     .ThenBy(r => r.Rank))
        {
            EventRankings.Add(row);
        }
    }

    private void RecalculateEventRankings()
    {
        if (_activeEvent is null)
            return;

        var rankingRows = new List<EventRankingEntry>();

        foreach (var classId in _activeEvent.ClassIds)
        {
            var cls = Classes.FirstOrDefault(c => c.Id == classId);
            var className = cls?.Name ??
                            _activeEvent.Sessions.FirstOrDefault(s => s.ClassId == classId)?.ClassName ??
                            "Class";

            var completedQuals = _activeEvent.Sessions
                .Where(s => s.SessionType == SessionType.Qualifying &&
                            s.ClassId == classId &&
                            s.ProgressStatus == EventSessionStatus.Completed &&
                            s.Results.Count > 0)
                .ToList();

            if (completedQuals.Count == 0)
                continue;

            var perDriver = new Dictionary<string, DriverRoundAggregate>(StringComparer.OrdinalIgnoreCase);

            foreach (var roundGroup in completedQuals.GroupBy(s => s.Round).OrderBy(g => g.Key))
            {
                var roundPerformances = new List<(string DriverId, string DriverName, int Laps, double? Elapsed, double? BestLap)>();

                foreach (var session in roundGroup)
                {
                    foreach (var result in session.Results)
                    {
                        var entrant = session.Entrants.FirstOrDefault(e => e.CarNumber == result.CarNumber);
                        if (entrant is null || string.IsNullOrWhiteSpace(entrant.DriverId))
                            continue;

                        var laps = session.Laps
                            .Where(l => l.CarNumber == result.CarNumber)
                            .OrderBy(l => l.LapNumber)
                            .ToList();

                        var elapsed = laps.Count == 0 ? (double?)null : laps[^1].Elapsed.TotalSeconds;
                        var bestLap = laps.Count == 0 ? (double?)null : laps.Min(l => l.LapTime.TotalSeconds);

                        roundPerformances.Add((
                            entrant.DriverId,
                            entrant.DisplayName,
                            laps.Count,
                            elapsed,
                            bestLap));
                    }
                }

                var ordered = roundPerformances
                    .GroupBy(p => p.DriverId, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g
                        .OrderByDescending(x => x.Laps)
                        .ThenBy(x => x.Elapsed ?? double.MaxValue)
                        .ThenBy(x => x.BestLap ?? double.MaxValue)
                        .First())
                    .OrderByDescending(x => x.Laps)
                    .ThenBy(x => x.Elapsed ?? double.MaxValue)
                    .ThenBy(x => x.BestLap ?? double.MaxValue)
                    .ThenBy(x => x.DriverName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (var i = 0; i < ordered.Count; i++)
                {
                    var perf = ordered[i];
                    if (!perDriver.TryGetValue(perf.DriverId, out var aggregate))
                    {
                        aggregate = new DriverRoundAggregate(
                            classId,
                            className,
                            perf.DriverId,
                            perf.DriverName,
                            []);
                        perDriver.Add(perf.DriverId, aggregate);
                    }

                    aggregate.Rounds.Add(new RoundPerformance(
                        roundGroup.Key,
                        i + 1,
                        perf.Laps,
                        perf.Elapsed,
                        perf.BestLap));
                }
            }

            var aggregates = perDriver.Values.ToList();
            aggregates.Sort(CompareDriverRanking);

            for (var i = 0; i < aggregates.Count; i++)
            {
                var aggregate = aggregates[i];
                var rounds = aggregate.Rounds.OrderBy(r => r.Round).ToList();
                var best = rounds.OrderBy(r => r.Position)
                    .ThenBy(r => r.ElapsedSeconds ?? double.MaxValue)
                    .ThenBy(r => r.BestLapSeconds ?? double.MaxValue)
                    .First();

                var latest = rounds.OrderByDescending(r => r.Round).First();
                var lapValues = rounds
                    .Where(r => r.BestLapSeconds.HasValue)
                    .Select(r => r.BestLapSeconds!.Value)
                    .ToList();

                rankingRows.Add(new EventRankingEntry
                {
                    ClassId = aggregate.ClassId,
                    ClassName = aggregate.ClassName,
                    Rank = i + 1,
                    DriverId = aggregate.DriverId,
                    DriverName = aggregate.DriverName,
                    RoundsCompleted = rounds.Count,
                    BestRoundPosition = best.Position,
                    LatestRoundPosition = latest.Position,
                    TotalLaps = rounds.Sum(r => r.Laps),
                    BestLapSeconds = lapValues.Count == 0 ? null : lapValues.Min(),
                    BestRoundElapsedSeconds = best.ElapsedSeconds,
                    AverageRoundPosition = rounds.Average(r => r.Position)
                });
            }
        }

        _activeEvent.Rankings = rankingRows;
    }

    private static int CompareDriverRanking(DriverRoundAggregate a, DriverRoundAggregate b)
    {
        var aPositions = a.Rounds.Select(r => r.Position).OrderBy(p => p).ToList();
        var bPositions = b.Rounds.Select(r => r.Position).OrderBy(p => p).ToList();
        var count = Math.Max(aPositions.Count, bPositions.Count);

        for (var i = 0; i < count; i++)
        {
            var ap = i < aPositions.Count ? aPositions[i] : int.MaxValue;
            var bp = i < bPositions.Count ? bPositions[i] : int.MaxValue;
            var compare = ap.CompareTo(bp);
            if (compare != 0)
                return compare;
        }

        var aBest = a.Rounds
            .OrderBy(r => r.Position)
            .ThenBy(r => r.ElapsedSeconds ?? double.MaxValue)
            .ThenBy(r => r.BestLapSeconds ?? double.MaxValue)
            .First();

        var bBest = b.Rounds
            .OrderBy(r => r.Position)
            .ThenBy(r => r.ElapsedSeconds ?? double.MaxValue)
            .ThenBy(r => r.BestLapSeconds ?? double.MaxValue)
            .First();

        var elapsedCompare = (aBest.ElapsedSeconds ?? double.MaxValue)
            .CompareTo(bBest.ElapsedSeconds ?? double.MaxValue);
        if (elapsedCompare != 0)
            return elapsedCompare;

        var lapCompare = (aBest.BestLapSeconds ?? double.MaxValue)
            .CompareTo(bBest.BestLapSeconds ?? double.MaxValue);
        if (lapCompare != 0)
            return lapCompare;

        return string.Compare(a.DriverName, b.DriverName, StringComparison.OrdinalIgnoreCase);
    }

    private async void ApplyEventRankingsToSeeds_Click(object sender, RoutedEventArgs e)
    {
        if (_activeEvent is null)
            return;

        RecalculateEventRankings();
        RefreshEventRankings();

        if (_activeEvent.Rankings.Count == 0)
        {
            MessageBox.Show(this,
                "There are no completed qualifying results to rank yet.",
                "Event rankings", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this,
                "Apply these event ranks to the drivers' class seed values?\n\n" +
                "Future qualifying rounds built with Ranked sorting will use these seed numbers.",
                "Apply rankings as seeds", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var updated = 0;
        foreach (var ranking in _activeEvent.Rankings)
        {
            foreach (var assignment in _driverAssignments.Where(a =>
                         a.DriverId == ranking.DriverId &&
                         a.ClassId == ranking.ClassId))
            {
                assignment.SeedRank = ranking.Rank;
                updated++;
            }
        }

        RefreshAssignmentRows();
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"Updated {updated} driver/class assignment seed value(s) from event rankings.";
    }
}
