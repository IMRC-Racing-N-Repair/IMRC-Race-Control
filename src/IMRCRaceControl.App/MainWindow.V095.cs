using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private sealed record ProjectionCandidate(
        string Key,
        string DriverId,
        string SessionId,
        string DriverName,
        int Heat,
        int CarNumber,
        int Laps,
        TimeSpan? Elapsed,
        int ComparisonLaps,
        TimeSpan? ComparisonTime,
        TimeSpan? BestLap,
        string PaceFinish,
        bool IsCurrentHeat,
        bool HasPerformance);

    private readonly Dictionary<string, int> _projectionPreviousPositions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (string State, string Display)> _projectionMovement =
        new(StringComparer.OrdinalIgnoreCase);

    private string _projectionMovementSessionId = string.Empty;
    private int _projectionMovementLapTotal = -1;
    private EventSessionStatus? _projectionMovementSessionStatus;

    private SpectatorDashboardWindow? _spectatorDashboardWindow;
    private string _raceSidePanel = "LAPS";
    private bool _raceSidePanelCollapsed;

    // -----------------------------------------------------------------
    // Right-side LAPS / POS tab rail
    // -----------------------------------------------------------------

    private void LapsSideTabButton_Click(object sender, RoutedEventArgs e) =>
        ToggleRaceSidePanel("LAPS");

    private void PositionSideTabButton_Click(object sender, RoutedEventArgs e) =>
        ToggleRaceSidePanel("POS");

    private void ToggleRaceSidePanel(string panel)
    {
        if (string.Equals(_raceSidePanel, panel, StringComparison.OrdinalIgnoreCase) &&
            !_raceSidePanelCollapsed)
        {
            _raceSidePanelCollapsed = true;
            RaceSidePanelContentHost.Visibility = Visibility.Collapsed;
            UpdateRaceSideTabAppearance();
            return;
        }

        _raceSidePanel = panel;
        _raceSidePanelCollapsed = false;
        RaceSidePanelContentHost.Visibility = Visibility.Visible;

        var showLaps = string.Equals(panel, "LAPS", StringComparison.OrdinalIgnoreCase);
        DriverLapPanelContent.Visibility = showLaps ? Visibility.Visible : Visibility.Collapsed;
        RoundPositionPanelContent.Visibility = showLaps ? Visibility.Collapsed : Visibility.Visible;

        if (showLaps)
            RefreshDriverLapPanel();
        else
            RefreshRoundProjection();

        UpdateRaceSideTabAppearance();
    }

    private void UpdateRaceSideTabAppearance()
    {
        var lapsActive =
            !_raceSidePanelCollapsed &&
            string.Equals(_raceSidePanel, "LAPS", StringComparison.OrdinalIgnoreCase);

        var posActive =
            !_raceSidePanelCollapsed &&
            string.Equals(_raceSidePanel, "POS", StringComparison.OrdinalIgnoreCase);

        LapsSideTabButton.Opacity = lapsActive ? 1.0 : 0.72;
        PositionSideTabButton.Opacity = posActive ? 1.0 : 0.72;
        LapsSideTabButton.FontWeight = lapsActive ? FontWeights.ExtraBold : FontWeights.Bold;
        PositionSideTabButton.FontWeight = posActive ? FontWeights.ExtraBold : FontWeights.Bold;
    }

    // -----------------------------------------------------------------
    // Live multi-heat overall-position projection
    // -----------------------------------------------------------------

    private void RefreshRoundProjection()
    {
        if (_activeEvent is null || _loadedSession is null)
        {
            ResetProjectionMovementTracking();
            ReplaceRoundProjectionRows([]);
            if (RoundProjectionInfoText is not null)
                RoundProjectionInfoText.Text = "Load a qualifying heat.";
            return;
        }

        if (_loadedSession.SessionType != SessionType.Qualifying)
        {
            ResetProjectionMovementTracking();
            ReplaceRoundProjectionRows([]);
            if (RoundProjectionInfoText is not null)
                RoundProjectionInfoText.Text =
                    "Live overall position is shown for qualifying rounds.";
            return;
        }

        var roundSessions = _activeEvent.Sessions
            .Where(s =>
                s.SessionType == SessionType.Qualifying &&
                s.ProgressStatus != EventSessionStatus.Skipped &&
                s.Round == _loadedSession.Round &&
                string.Equals(s.ClassId, _loadedSession.ClassId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Heat)
            .ThenBy(s => s.QueueOrder)
            .ToList();

        if (roundSessions.Count == 0)
        {
            ResetProjectionMovementTracking();
            ReplaceRoundProjectionRows([]);
            RoundProjectionInfoText.Text = "No qualifying heats found for this round.";
            return;
        }

        var candidates = new List<ProjectionCandidate>();

        foreach (var session in roundSessions)
        {
            var current = string.Equals(
                session.Id,
                _loadedSession.Id,
                StringComparison.OrdinalIgnoreCase);

            foreach (var entrant in session.Entrants.OrderBy(e => e.CarNumber))
            {
                var laps = 0;
                TimeSpan? elapsed = null;
                var comparisonLaps = 0;
                TimeSpan? comparisonTime = null;
                TimeSpan? bestLap = null;
                var paceFinish = "—";
                var hasPerformance = false;

                if (current && session.ProgressStatus != EventSessionStatus.Completed && !_recallMode)
                {
                    var standing = Standings.FirstOrDefault(s => s.CarNumber == entrant.CarNumber);
                    var engineLaps = _raceEngine.Laps
                        .Where(l => l.CarNumber == entrant.CarNumber)
                        .OrderBy(l => l.LapNumber)
                        .ToList();

                    laps = standing?.Laps ?? engineLaps.Count;
                    elapsed = engineLaps.LastOrDefault()?.Elapsed;
                    bestLap = engineLaps.Count == 0 ? null : engineLaps.Min(l => l.LapTime);
                    hasPerformance = laps > 0;

                    if (hasPerformance)
                    {
                        var raceFinished =
                            _raceEngine.State == RaceState.Complete ||
                            session.ProgressStatus == EventSessionStatus.Completed;

                        paceFinish = raceFinished && elapsed.HasValue
                            ? $"{laps}/{Core.RaceEngine.FormatElapsed(elapsed.Value)}"
                            : !string.IsNullOrWhiteSpace(standing?.Pace) && standing.Pace != "—"
                                ? standing.Pace
                                : elapsed.HasValue
                                    ? $"{laps}/{Core.RaceEngine.FormatElapsed(elapsed.Value)}"
                                    : "—";

                        if (raceFinished)
                        {
                            comparisonLaps = laps;
                            comparisonTime = elapsed;
                        }
                        else if (TryParsePaceResult(paceFinish, out var projectedLaps, out var projectedTime))
                        {
                            comparisonLaps = projectedLaps;
                            comparisonTime = projectedTime;
                        }
                        else
                        {
                            comparisonLaps = laps;
                            comparisonTime = elapsed;
                        }
                    }
                }
                else if (session.ProgressStatus == EventSessionStatus.Completed)
                {
                    var result = session.Results.FirstOrDefault(r => r.CarNumber == entrant.CarNumber);
                    var storedLaps = session.Laps
                        .Where(l => l.CarNumber == entrant.CarNumber)
                        .OrderBy(l => l.LapNumber)
                        .ToList();

                    laps = result?.Laps ?? storedLaps.Count;
                    elapsed = storedLaps.LastOrDefault()?.Elapsed;
                    bestLap = storedLaps.Count == 0 ? null : storedLaps.Min(l => l.LapTime);
                    hasPerformance = result is not null || storedLaps.Count > 0;

                    if (hasPerformance)
                    {
                        var total = result?.TotalTime;
                        if (string.IsNullOrWhiteSpace(total) || total == "—")
                            total = elapsed.HasValue ? Core.RaceEngine.FormatElapsed(elapsed.Value) : "—";

                        paceFinish = total == "—" ? "—" : $"{laps}/{total}";
                        comparisonLaps = laps;
                        comparisonTime = elapsed;

                        if (TryParsePaceResult(paceFinish, out var finishedLaps, out var finishedTime))
                        {
                            comparisonLaps = finishedLaps;
                            comparisonTime = finishedTime;
                        }
                    }
                }
                else if (current)
                {
                    var standing = Standings.FirstOrDefault(s => s.CarNumber == entrant.CarNumber);
                    laps = standing?.Laps ?? 0;
                    hasPerformance = laps > 0;
                    paceFinish = hasPerformance && !string.IsNullOrWhiteSpace(standing?.Pace)
                        ? standing.Pace
                        : "—";

                    if (TryParsePaceResult(paceFinish, out var projectedLaps, out var projectedTime))
                    {
                        comparisonLaps = projectedLaps;
                        comparisonTime = projectedTime;
                    }
                    else
                    {
                        comparisonLaps = laps;
                    }
                }

                candidates.Add(new ProjectionCandidate(
                    $"{session.Id}:{entrant.DriverId}:{entrant.CarNumber}",
                    entrant.DriverId,
                    session.Id,
                    entrant.DisplayName,
                    session.Heat,
                    entrant.CarNumber,
                    laps,
                    elapsed,
                    comparisonLaps,
                    comparisonTime,
                    bestLap,
                    paceFinish,
                    current,
                    hasPerformance));
            }
        }

        var ranked = candidates
            .Where(c => c.HasPerformance)
            .OrderByDescending(c => c.ComparisonLaps)
            .ThenBy(c => c.ComparisonTime ?? TimeSpan.MaxValue)
            .ThenBy(c => c.BestLap ?? TimeSpan.MaxValue)
            .ThenBy(c => c.Heat)
            .ThenBy(c => c.CarNumber)
            .ToList();

        var projectedPositionByKey = ranked
            .Select((candidate, index) => new { candidate.Key, Position = index + 1 })
            .ToDictionary(x => x.Key, x => x.Position, StringComparer.OrdinalIgnoreCase);

        UpdateProjectionMovement(candidates, projectedPositionByKey);

        var rows = candidates
            .Select(c =>
            {
                var hasMovement =
                    _projectionMovement.TryGetValue(c.Key, out var movement) &&
                    c.IsCurrentHeat &&
                    c.HasPerformance;

                return new RoundProjectionRow
                {
                    Key = c.Key,
                    DriverId = c.DriverId,
                    SessionId = c.SessionId,
                    DriverName = c.DriverName,
                    PaceFinish = c.PaceFinish,
                    IsCurrentHeat = c.IsCurrentHeat,
                    MovementState = hasMovement ? movement.State : string.Empty,
                    MovementDisplay = hasMovement ? movement.Display : "—",
                    ProjectedPosition = projectedPositionByKey.TryGetValue(c.Key, out var position)
                        ? position
                        : null
                };
            })
            .OrderBy(r => r.ProjectedPosition ?? int.MaxValue)
            .ThenByDescending(r => r.IsCurrentHeat)
            .ThenBy(r => r.DriverName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReplaceRoundProjectionRows(rows);

        RoundProjectionInfoText.Text = roundSessions.Count > 1
            ? $"{_loadedSession.ClassName} • Q{_loadedSession.Round} • {roundSessions.Count} heats • PACE while live, finished LAPS/TIME when complete"
            : $"{_loadedSession.ClassName} • Q{_loadedSession.Round} • PACE while live, finished LAPS/TIME when complete";
    }

    private static bool TryParsePaceResult(
        string? value,
        out int laps,
        out TimeSpan totalTime)
    {
        laps = 0;
        totalTime = TimeSpan.Zero;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Trim().Split(
            '/',
            2,
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2 || !int.TryParse(parts[0], out laps) || laps <= 0)
            return false;

        var timeParts = parts[1].Split(':', StringSplitOptions.TrimEntries);

        if (timeParts.Length == 2 &&
            int.TryParse(timeParts[0], out var minutes) &&
            double.TryParse(
                timeParts[1],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var seconds) &&
            minutes >= 0 &&
            seconds is >= 0 and < 60)
        {
            totalTime = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
            return totalTime > TimeSpan.Zero;
        }

        if (double.TryParse(
                parts[1],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var totalSeconds) &&
            totalSeconds > 0)
        {
            totalTime = TimeSpan.FromSeconds(totalSeconds);
            return true;
        }

        return false;
    }

    private void UpdateProjectionMovement(
        IReadOnlyList<ProjectionCandidate> candidates,
        IReadOnlyDictionary<string, int> projectedPositionByKey)
    {
        if (_loadedSession is null)
        {
            ResetProjectionMovementTracking();
            return;
        }

        var currentCandidates = candidates
            .Where(c => c.IsCurrentHeat && c.HasPerformance)
            .ToList();

        var currentLapTotal = currentCandidates.Sum(c => c.Laps);
        var currentStatus = _loadedSession.ProgressStatus;
        var sessionChanged = !string.Equals(
            _projectionMovementSessionId,
            _loadedSession.Id,
            StringComparison.OrdinalIgnoreCase);

        if (sessionChanged)
        {
            _projectionPreviousPositions.Clear();
            _projectionMovement.Clear();
            _projectionMovementSessionId = _loadedSession.Id;
            _projectionMovementLapTotal = currentLapTotal;
            _projectionMovementSessionStatus = currentStatus;

            foreach (var candidate in currentCandidates)
            {
                if (!projectedPositionByKey.TryGetValue(candidate.Key, out var position))
                    continue;

                _projectionPreviousPositions[candidate.Key] = position;
                _projectionMovement[candidate.Key] = ("SAME", "SAME");
            }

            return;
        }

        var meaningfulUpdate =
            currentLapTotal != _projectionMovementLapTotal ||
            currentStatus != _projectionMovementSessionStatus;

        if (!meaningfulUpdate)
            return;

        foreach (var candidate in currentCandidates)
        {
            if (!projectedPositionByKey.TryGetValue(candidate.Key, out var currentPosition))
                continue;

            if (_projectionPreviousPositions.TryGetValue(candidate.Key, out var previousPosition))
            {
                var delta = previousPosition - currentPosition;

                _projectionMovement[candidate.Key] = delta switch
                {
                    > 0 => ("UP", $"UP {delta}"),
                    < 0 => ("DOWN", $"DOWN {Math.Abs(delta)}"),
                    _ => ("SAME", "SAME")
                };
            }
            else
            {
                _projectionMovement[candidate.Key] = ("SAME", "SAME");
            }

            _projectionPreviousPositions[candidate.Key] = currentPosition;
        }

        // Remove stale movement state when the current heat roster changes.
        var currentKeys = currentCandidates
            .Select(c => c.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in _projectionMovement.Keys.Where(k => !currentKeys.Contains(k)).ToList())
            _projectionMovement.Remove(key);

        foreach (var key in _projectionPreviousPositions.Keys.Where(k => !currentKeys.Contains(k)).ToList())
            _projectionPreviousPositions.Remove(key);

        _projectionMovementLapTotal = currentLapTotal;
        _projectionMovementSessionStatus = currentStatus;
    }

    private void ResetProjectionMovementTracking()
    {
        _projectionPreviousPositions.Clear();
        _projectionMovement.Clear();
        _projectionMovementSessionId = string.Empty;
        _projectionMovementLapTotal = -1;
        _projectionMovementSessionStatus = null;
    }

    private void ReplaceRoundProjectionRows(IReadOnlyList<RoundProjectionRow> rows)
    {
        var incomingKeys = rows
            .Select(r => r.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var i = RoundProjectionRows.Count - 1; i >= 0; i--)
        {
            if (!incomingKeys.Contains(RoundProjectionRows[i].Key))
                RoundProjectionRows.RemoveAt(i);
        }

        for (var targetIndex = 0; targetIndex < rows.Count; targetIndex++)
        {
            var snapshot = rows[targetIndex];
            var existing = RoundProjectionRows.FirstOrDefault(r =>
                string.Equals(r.Key, snapshot.Key, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                RoundProjectionRows.Insert(
                    Math.Min(targetIndex, RoundProjectionRows.Count),
                    snapshot);
                continue;
            }

            existing.CopyFrom(snapshot);

            var currentIndex = RoundProjectionRows.IndexOf(existing);
            if (currentIndex != targetIndex &&
                currentIndex >= 0 &&
                targetIndex < RoundProjectionRows.Count)
            {
                RoundProjectionRows.Move(currentIndex, targetIndex);
            }
        }
    }

    // -----------------------------------------------------------------
    // Read-only second-screen dashboard
    // -----------------------------------------------------------------

    private void PopOutDashboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_spectatorDashboardWindow is not null)
        {
            if (_spectatorDashboardWindow.WindowState == WindowState.Minimized)
                _spectatorDashboardWindow.WindowState = WindowState.Normal;

            _spectatorDashboardWindow.Activate();
            return;
        }

        _spectatorDashboardWindow = new SpectatorDashboardWindow(this)
        {
            Owner = this
        };

        _spectatorDashboardWindow.Closed += (_, _) =>
            _spectatorDashboardWindow = null;

        _spectatorDashboardWindow.Show();
    }

    internal bool SpectatorIsPractice => _finishedSpectatorFrame is null && _practiceActive;
    internal ObservableCollection<StandingRow> SpectatorStandings => _finishedSpectatorFrame?.Rows ?? Standings;
    internal ObservableCollection<RoundProjectionRow> SpectatorRoundPositions => _finishedSpectatorFrame?.Positions ?? RoundProjectionRows;

    internal string SpectatorRaceHeader => _finishedSpectatorFrame?.Header ?? (
        _loadedSession is null
            ? "NO RACE SELECTED"
            : $"{_loadedSession.RoundLabel}  •  RACE {_loadedSession.RaceNumber}  •  " + (_loadedSession.SessionType == SessionType.Race ? $"{_loadedSession.HeatMainDisplay}-MAIN" : $"HEAT {_loadedSession.Heat}"));

    internal string SpectatorRaceSubheader => _finishedSpectatorFrame?.Subheader ??
        _loadedSession?.Name ?? "Load a race from Race Control";

    internal string SpectatorEventText => _finishedSpectatorFrame?.Event ?? _activeEvent?.Name ?? "IMRC Race Control";
    internal string SpectatorMasterClock => _finishedSpectatorFrame?.Clock ?? RaceClockText?.Text ?? "0:00.0";
    internal string SpectatorRaceState => _finishedSpectatorFrame?.State ?? RaceStateText?.Text ?? "READY";
    internal string SpectatorFastestText => _finishedSpectatorFrame?.Fastest ?? FastestSessionText?.Text ?? "Fastest: —";
    internal string SpectatorProjectionText => _finishedSpectatorFrame?.Projection ?? RoundProjectionInfoText?.Text ?? string.Empty;
}
