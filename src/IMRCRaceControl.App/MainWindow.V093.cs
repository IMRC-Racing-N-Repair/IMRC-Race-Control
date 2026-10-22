using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    // -----------------------------------------------------------------
    // Explicit race navigation
    // -----------------------------------------------------------------

    private void PreviousRaceButton_Click(object sender, RoutedEventArgs e) =>
        NavigateRaceDashboard(-1);

    private void NextRaceButton_Click(object sender, RoutedEventArgs e) =>
        NavigateRaceDashboard(1);

    private void NavigateRaceDashboard(int offset)
    {
        if (_activeEvent is null || _loadedSession is null)
            return;

        if (_resultAutoSaveInProgress)
        {
            FooterStatusText.Text = "Wait for the current result to finish saving before changing races.";
            return;
        }

        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            MessageBox.Show(this,
                "End, false-start, or restart the current race before moving to another race.",
                "Race in progress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var sessions = GetQueueSessions()
            .Where(s => s.ProgressStatus != EventSessionStatus.Skipped)
            .OrderBy(s => s.QueueOrder)
            .ToList();

        var index = sessions.FindIndex(s =>
            string.Equals(s.Id, _loadedSession.Id, StringComparison.OrdinalIgnoreCase));

        var targetIndex = index + offset;
        if (index < 0 || targetIndex < 0 || targetIndex >= sessions.Count)
            return;

        if (offset > 0) ReleaseFinishedSpectatorFrame();
        LoadSessionIntoRaceControl(sessions[targetIndex]);
        RefreshRaceDayQueue();
    }

    // -----------------------------------------------------------------
    // Driver lap dropdown / corrections
    // -----------------------------------------------------------------

    private void DriverLapDriverCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents)
            return;

        if (DriverLapDriverCombo.SelectedItem is not RacerEntry racer)
            return;

        _selectedDriverLapCarNumber = racer.CarNumber;
        RefreshDriverLapPanel();
    }

    private void DriverLapGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriverLapGrid.SelectedItem is DriverLapViewRow row)
            LapCorrectionTimeText.Text = row.LapTime == "—" ? string.Empty : row.LapTime;

        RefreshLapCorrectionAvailability();
    }

    private async void AddLapCorrection_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCorrectStoredLaps(out var session))
            return;

        if (!_selectedDriverLapCarNumber.HasValue)
        {
            FooterStatusText.Text = "Select a driver before adding a lap.";
            return;
        }

        if (!TryParseLapTime(LapCorrectionTimeText.Text, out var lapTime))
        {
            ShowLapTimeFormatMessage();
            return;
        }

        var carNumber = _selectedDriverLapCarNumber.Value;
        var entrant = session.Entrants.FirstOrDefault(e => e.CarNumber == carNumber);
        if (entrant is null)
            return;

        var driverLaps = session.Laps
            .Where(l => l.CarNumber == carNumber)
            .OrderBy(l => l.LapNumber)
            .ToList();

        var personalStart = InferPersonalStart(session, driverLaps);
        var newSequence = session.Laps.Select(l => l.Sequence).DefaultIfEmpty(0).Max() + 1;

        var insertIndex = driverLaps.Count;
        if (DriverLapGrid.SelectedItem is DriverLapViewRow selected &&
            !selected.IsBreakout &&
            selected.CarNumber == carNumber)
        {
            var selectedIndex = driverLaps.FindIndex(l => l.Sequence == selected.Sequence);
            if (selectedIndex >= 0)
                insertIndex = selectedIndex + 1;
        }

        // If a missed pass is inserted between two existing laps, the next
        // stored lap commonly represents the combined duration. Subtract the
        // inserted lap when possible so the driver's total elapsed stays intact.
        if (insertIndex < driverLaps.Count &&
            driverLaps[insertIndex].LapTime > lapTime)
        {
            driverLaps[insertIndex] = driverLaps[insertIndex] with
            {
                LapTime = driverLaps[insertIndex].LapTime - lapTime,
                Source = "Operator Correction"
            };
        }

        driverLaps.Insert(insertIndex, new LapRecord(
            newSequence,
            carNumber,
            entrant.DisplayName,
            insertIndex + 1,
            lapTime,
            TimeSpan.Zero,
            personalStart,
            "Operator Correction",
            false));

        RebuildDriverLapTimeline(session, carNumber, driverLaps, personalStart, markFinalFinished: true);
        await CommitLapCorrectionsAsync(session, $"Added {RaceEngine.FormatLap(lapTime)} to {entrant.DisplayName}.");
    }

    private async void ChangeLapCorrection_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCorrectStoredLaps(out var session) ||
            DriverLapGrid.SelectedItem is not DriverLapViewRow selected)
            return;

        if (!TryParseLapTime(LapCorrectionTimeText.Text, out var newLapTime))
        {
            ShowLapTimeFormatMessage();
            return;
        }

        if (selected.IsBreakout)
        {
            var index = session.Breakouts.FindIndex(b => b.Sequence == selected.Sequence);
            if (index < 0)
                return;

            session.Breakouts[index] = session.Breakouts[index] with
            {
                LapTime = newLapTime,
                Source = "Operator Correction"
            };

            await CommitLapCorrectionsAsync(
                session,
                $"Changed breakout observation to {RaceEngine.FormatLap(newLapTime)}.");
            return;
        }

        var carNumber = selected.CarNumber;
        var driverLaps = session.Laps
            .Where(l => l.CarNumber == carNumber)
            .OrderBy(l => l.LapNumber)
            .ToList();

        var targetIndex = driverLaps.FindIndex(l => l.Sequence == selected.Sequence);
        if (targetIndex < 0)
            return;

        driverLaps[targetIndex] = driverLaps[targetIndex] with
        {
            LapTime = newLapTime,
            Source = "Operator Correction"
        };

        var personalStart = InferPersonalStart(session, driverLaps);
        RebuildDriverLapTimeline(session, carNumber, driverLaps, personalStart, markFinalFinished: true);

        await CommitLapCorrectionsAsync(
            session,
            $"Changed Car {carNumber} Lap {targetIndex + 1} to {RaceEngine.FormatLap(newLapTime)}.");
    }

    private async void DeleteLapCorrection_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCorrectStoredLaps(out var session) ||
            DriverLapGrid.SelectedItem is not DriverLapViewRow selected)
            return;

        if (MessageBox.Show(this,
                selected.IsBreakout
                    ? $"Delete the selected breakout observation ({selected.LapTime})?"
                    : $"Delete Lap {selected.LapNumber} ({selected.LapTime})?",
                "Delete lap",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        if (selected.IsBreakout)
        {
            session.Breakouts.RemoveAll(b => b.Sequence == selected.Sequence);
            await CommitLapCorrectionsAsync(session, "Breakout observation deleted.");
            return;
        }

        var carNumber = selected.CarNumber;
        var driverLaps = session.Laps
            .Where(l => l.CarNumber == carNumber)
            .OrderBy(l => l.LapNumber)
            .ToList();

        var targetIndex = driverLaps.FindIndex(l => l.Sequence == selected.Sequence);
        if (targetIndex < 0)
            return;

        var removed = driverLaps[targetIndex];

        // Deleting a false intermediate pass should preserve the racer's total
        // elapsed time, so its duration is merged into the following scored lap.
        if (targetIndex + 1 < driverLaps.Count)
        {
            var next = driverLaps[targetIndex + 1];
            driverLaps[targetIndex + 1] = next with
            {
                LapTime = next.LapTime + removed.LapTime,
                Source = "Operator Correction"
            };
        }

        driverLaps.RemoveAt(targetIndex);

        var personalStart = InferPersonalStart(session, driverLaps);
        RebuildDriverLapTimeline(session, carNumber, driverLaps, personalStart, markFinalFinished: true);

        await CommitLapCorrectionsAsync(
            session,
            $"Deleted Car {carNumber} Lap {selected.LapNumber}.");
    }

    private bool CanCorrectStoredLaps(out EventSessionDefinition session)
    {
        session = _loadedSession!;

        if (_loadedSession is null ||
            _loadedSession.ProgressStatus != EventSessionStatus.Completed ||
            _resultAutoSaveInProgress ||
            _raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            FooterStatusText.Text = "Lap corrections are available after the selected race is complete.";
            return false;
        }

        return true;
    }

    private static bool TryParseLapTime(string? text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        var input = text?.Trim() ?? string.Empty;
        if (input.Length == 0)
            return false;

        if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
            seconds > 0)
        {
            value = TimeSpan.FromSeconds(seconds);
            return true;
        }

        var parts = input.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var remainingSeconds) &&
            minutes >= 0 &&
            remainingSeconds is >= 0 and < 60)
        {
            value = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(remainingSeconds);
            return value > TimeSpan.Zero;
        }

        return false;
    }

    private void ShowLapTimeFormatMessage() =>
        MessageBox.Show(this,
            "Enter the lap time as seconds (example 3.422) or minutes:seconds (example 1:03.422).",
            "Lap correction",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private static DateTimeOffset InferPersonalStart(
        EventSessionDefinition session,
        IReadOnlyList<LapRecord> driverLaps)
    {
        if (driverLaps.Count > 0)
            return driverLaps[0].Timestamp - driverLaps[0].Elapsed;

        return session.StartedAt
               ?? session.CompletedAt
               ?? DateTimeOffset.Now;
    }

    private static void RebuildDriverLapTimeline(
        EventSessionDefinition session,
        int carNumber,
        IReadOnlyList<LapRecord> driverLaps,
        DateTimeOffset personalStart,
        bool markFinalFinished)
    {
        session.Laps.RemoveAll(l => l.CarNumber == carNumber);

        var elapsed = TimeSpan.Zero;
        for (var i = 0; i < driverLaps.Count; i++)
        {
            var source = driverLaps[i];
            elapsed += source.LapTime;
            var finished = markFinalFinished && i == driverLaps.Count - 1;

            session.Laps.Add(source with
            {
                LapNumber = i + 1,
                Elapsed = elapsed,
                Timestamp = personalStart + elapsed,
                Finished = finished
            });
        }

        NormalizeSessionLapSequences(session);
    }

    private static void NormalizeSessionLapSequences(EventSessionDefinition session)
    {
        var ordered = session.Laps
            .OrderBy(l => l.Timestamp)
            .ThenBy(l => l.CarNumber)
            .ThenBy(l => l.LapNumber)
            .ToList();

        session.Laps = ordered
            .Select((lap, index) => lap with { Sequence = index + 1L })
            .ToList();
    }

    private async Task CommitLapCorrectionsAsync(EventSessionDefinition session, string message)
    {
        RebuildStoredSessionResults(session);

        _recallMode = true;
        _loadedSession = session;

        Standings.Clear();
        foreach (var row in session.Results.OrderBy(r => r.Position))
        {
            row.IsCheckedIn = true;
            row.ManualActionText = "PASS";
            Standings.Add(row);
        }

        RecentLaps.Clear();
        foreach (var lap in session.Laps.OrderByDescending(l => l.Sequence).Take(250))
            RecentLaps.Add(lap);

        RecalculateEventRankings();
        RefreshEventRankings();
        RefreshPlannedSessions(session.Id);
        RefreshRaceDayQueue();
        RenderRecalledRace();
        RefreshDriverLapPanel();

        if (_activeEvent?.Status == EventStatus.Completed)
        {
            try
            {
                _activeEvent.LastArchivePath = await _eventArchiveService.SaveAsync(_activeEvent);
            }
            catch
            {
                // The SQLite state remains authoritative even if a secondary
                // exported archive cannot be refreshed.
            }
        }

        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"{message} Heat results and rankings recalculated.";
    }

    private void RebuildStoredSessionResults(EventSessionDefinition session)
    {
        var sessionBest = session.Laps.Count == 0
            ? (TimeSpan?)null
            : session.Laps.Min(l => l.LapTime);

        var racers = session.Entrants
            .Select(entrant =>
            {
                var laps = session.Laps
                    .Where(l => l.CarNumber == entrant.CarNumber)
                    .OrderBy(l => l.LapNumber)
                    .ToList();

                return new StoredRacerResult(entrant, laps);
            })
            .OrderByDescending(x => x.Laps.Count)
            .ThenBy(x => x.Laps.LastOrDefault()?.Elapsed ?? TimeSpan.MaxValue)
            .ThenBy(x => x.Entrant.CarNumber)
            .ToList();

        var cls = Classes.FirstOrDefault(c => c.Id == session.ClassId);
        var duration = TimeSpan.FromMinutes(session.RaceMinutesOverride ?? cls?.MinutesFor(session.SessionType) ?? 0);
        var raceFormat = session.RaceFormat;
        var targetLaps = session.TargetLaps > 0
            ? session.TargetLaps
            : cls?.TargetLaps ?? 50;

        var results = new List<StandingRow>();
        for (var i = 0; i < racers.Count; i++)
        {
            var racer = racers[i];
            var laps = racer.Laps;
            var last = laps.LastOrDefault();
            var best = laps.Count == 0 ? (TimeSpan?)null : laps.Min(l => l.LapTime);
            var bestLapNumber = best.HasValue
                ? laps.First(l => l.LapTime == best.Value).LapNumber
                : 0;
            var average = AverageLap(laps);

            results.Add(new StandingRow
            {
                Position = i + 1,
                CarNumber = racer.Entrant.CarNumber,
                DriverName = racer.Entrant.DisplayName,
                Laps = laps.Count,
                Clock = "0:00.0",
                LastLap = last is null ? "—" : RaceEngine.FormatLap(last.LapTime),
                BestLap = best.HasValue ? RaceEngine.FormatLap(best.Value) : "—",
                BestLapNumber = bestLapNumber <= 0 ? "—" : bestLapNumber.ToString(),
                Top2 = StoredBestConsecutive(laps, 2),
                Top3 = StoredBestConsecutive(laps, 3),
                Top5 = StoredBestConsecutive(laps, 5),
                AverageLap = average.HasValue ? RaceEngine.FormatLap(average.Value) : "—",
                Average5 = StoredRecentAverage(laps, 5),
                Average10 = StoredRecentAverage(laps, 10),
                Average15 = StoredRecentAverage(laps, 15),
                Consistency = StoredConsistency(laps),
                Pace = StoredPace(laps, average, duration, raceFormat, targetLaps),
                GapToLeader = i == 0 ? "LEADER" : StoredGap(racers[0].Laps, laps),
                GapBehind = i == racers.Count - 1 ? "—" : StoredGap(laps, racers[i + 1].Laps),
                TotalTime = last is null ? "—" : RaceEngine.FormatElapsed(last.Elapsed),
                Status = "COMPLETE",
                ManualActionText = "PASS",
                IsFastestLap = best.HasValue && sessionBest.HasValue && best.Value == sessionBest.Value,
                IsNewPersonalBest = false,
                IsCheckedIn = true
            });
        }

        session.Results = results;
    }

    private sealed record StoredRacerResult(SessionEntrant Entrant, List<LapRecord> Laps);

    private static TimeSpan? AverageLap(IReadOnlyList<LapRecord> laps)
    {
        if (laps.Count == 0)
            return null;
        return TimeSpan.FromTicks((long)laps.Average(l => l.LapTime.Ticks));
    }

    private static string StoredBestConsecutive(IReadOnlyList<LapRecord> laps, int count)
    {
        if (laps.Count < count)
            return "—";

        long bestTicks = long.MaxValue;
        var start = 0;
        for (var i = 0; i <= laps.Count - count; i++)
        {
            long total = 0;
            for (var j = 0; j < count; j++)
                total += laps[i + j].LapTime.Ticks;

            if (total < bestTicks)
            {
                bestTicks = total;
                start = i;
            }
        }

        var avg = TimeSpan.FromTicks(bestTicks / count);
        return $"{RaceEngine.FormatLap(avg)} L{laps[start].LapNumber}-{laps[start + count - 1].LapNumber}";
    }

    private static string StoredRecentAverage(IReadOnlyList<LapRecord> laps, int count)
    {
        if (laps.Count < count)
            return "—";

        var avgTicks = (long)laps
            .Skip(laps.Count - count)
            .Average(l => l.LapTime.Ticks);

        return RaceEngine.FormatLap(TimeSpan.FromTicks(avgTicks));
    }

    private static string StoredConsistency(IReadOnlyList<LapRecord> laps)
    {
        if (laps.Count < 2)
            return "—";

        var values = laps.Select(l => l.LapTime.TotalSeconds).ToArray();
        var avg = values.Average();
        if (avg <= 0)
            return "—";

        var variance = values.Average(v => Math.Pow(v - avg, 2));
        var stdDev = Math.Sqrt(variance);
        var consistency = Math.Clamp(100.0 - (stdDev / avg * 100.0), 0.0, 100.0);
        return $"{consistency:0.0}%";
    }

    private static string StoredPace(
        IReadOnlyList<LapRecord> laps,
        TimeSpan? average,
        TimeSpan duration,
        RaceFormat raceFormat,
        int targetLaps)
    {
        if (laps.Count == 0 || !average.HasValue || average.Value <= TimeSpan.Zero)
            return "—";

        if (raceFormat == RaceFormat.Laps)
        {
            var projectedLaps = Math.Max(1, targetLaps);
            var projectedTime = TimeSpan.FromTicks(average.Value.Ticks * projectedLaps);
            return $"{projectedLaps}/{RaceEngine.FormatElapsed(projectedTime)}";
        }

        if (duration <= TimeSpan.Zero)
            return "—";

        var projected = Math.Max(laps.Count, (int)Math.Floor(duration.TotalSeconds / average.Value.TotalSeconds));
        var projectedTimeTimed = TimeSpan.FromTicks(average.Value.Ticks * projected);
        return $"{projected}/{RaceEngine.FormatElapsed(projectedTimeTimed)}";
    }

    private static string StoredGap(
        IReadOnlyList<LapRecord> ahead,
        IReadOnlyList<LapRecord> behind)
    {
        var lapDifference = ahead.Count - behind.Count;
        if (lapDifference > 0)
            return lapDifference == 1 ? "1 LAP" : $"{lapDifference} LAPS";

        var aheadLast = ahead.LastOrDefault();
        var behindLast = behind.LastOrDefault();
        if (aheadLast is null || behindLast is null)
            return "—";

        var gap = behindLast.Elapsed - aheadLast.Elapsed;
        if (gap < TimeSpan.Zero)
            gap = gap.Negate();

        return $"+{gap.TotalSeconds:0.000}";
    }
}
