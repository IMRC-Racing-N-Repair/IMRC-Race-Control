using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private bool _applyingDashboardColumns;
    private readonly Dictionary<int, DateTimeOffset> _passingHighlightUntilByCar = new();
    private readonly Dictionary<int, DateTimeOffset> _minimumLapGlowUntilByCar = new();
    private readonly RaceCheckInState _raceCheckIn = new();
    private HashSet<int> _checkedInCars => _raceCheckIn.CheckedCars;
    private string _checkInSessionId => _raceCheckIn.SessionId;
    private DispatcherTimer? _passingHighlightTimer;
    private DispatcherTimer? _minimumLapGlowTimer;
    private static readonly TimeSpan PassingHighlightDuration = TimeSpan.FromSeconds(2.25);

    private void ApplyDashboardPreferences()
    {
        if (StandingsGrid is null)
            return;

        ThemeService.ApplyDashboard(_settings);

        var columns = StandingsGrid.Columns
            .Where(c => c.Header is not null)
            .ToDictionary(c => c.Header.ToString() ?? string.Empty, c => c, StringComparer.OrdinalIgnoreCase);

        _applyingDashboardColumns = true;
        try
        {
            StandingsGrid.FrozenColumnCount = 0;

            var displayOrder = new List<string>();
            displayOrder.AddRange(DashboardColumnCatalog.LockedColumns);
            displayOrder.AddRange(_settings.DashboardColumnOrder
                .Where(id => DashboardColumnCatalog.OptionalColumns.Contains(id, StringComparer.OrdinalIgnoreCase)));
            displayOrder.AddRange(DashboardColumnCatalog.OptionalColumns
                .Where(id => !displayOrder.Contains(id, StringComparer.OrdinalIgnoreCase)));

            var visible = _settings.DashboardVisibleColumns
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < displayOrder.Count; i++)
            {
                if (!columns.TryGetValue(displayOrder[i], out var column))
                    continue;

                column.DisplayIndex = i;
                if (DashboardColumnCatalog.LockedColumns.Contains(displayOrder[i], StringComparer.OrdinalIgnoreCase))
                {
                    column.Visibility = Visibility.Visible;
                    column.CanUserReorder = false;
                }
                else
                {
                    column.Visibility = visible.Contains(displayOrder[i])
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    column.CanUserReorder = true;
                }
            }

            StandingsGrid.FrozenColumnCount = 3;
        }
        finally
        {
            StandingsGrid.FrozenColumnCount = 3;
            _applyingDashboardColumns = false;
        }
    }

    private void StandingsGrid_ColumnReordered(object sender, DataGridColumnEventArgs e)
    {
        if (_applyingDashboardColumns || !IsLoaded)
            return;

        Dispatcher.BeginInvoke(new Action(PersistDashboardColumnOrder));
    }

    private void PersistDashboardColumnOrder()
    {
        if (_applyingDashboardColumns)
            return;

        _applyingDashboardColumns = true;
        try
        {
            StandingsGrid.FrozenColumnCount = 0;

            var columns = StandingsGrid.Columns
                .Where(c => c.Header is not null)
                .ToDictionary(c => c.Header.ToString() ?? string.Empty, c => c, StringComparer.OrdinalIgnoreCase);

            // These three columns are permanent and cannot be moved.
            for (var i = 0; i < DashboardColumnCatalog.LockedColumns.Count; i++)
            {
                var id = DashboardColumnCatalog.LockedColumns[i];
                if (columns.TryGetValue(id, out var locked))
                {
                    locked.DisplayIndex = i;
                    locked.CanUserReorder = false;
                    locked.Visibility = Visibility.Visible;
                }
            }

            StandingsGrid.FrozenColumnCount = 3;

            _settings.DashboardColumnOrder = StandingsGrid.Columns
                .Where(c => c.Header is not null &&
                            DashboardColumnCatalog.OptionalColumns.Contains(c.Header.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                .OrderBy(c => c.DisplayIndex)
                .Select(c => c.Header!.ToString()!)
                .ToList();
        }
        finally
        {
            StandingsGrid.FrozenColumnCount = 3;
            _applyingDashboardColumns = false;
        }

        _ = SaveSettingsSafelyAsync();
        FooterStatusText.Text = "Race Dashboard column order saved.";
    }

    private void PrepareCheckInForSession(string sessionId, bool restart = false)
    {
        _raceCheckIn.SelectSession(sessionId, restart);

        foreach (var row in Standings)
        {
            row.IsCheckedIn = _checkedInCars.Contains(row.CarNumber);
            row.ManualActionText = "CHECK IN";
        }
    }

    private bool MarkDriverCheckedIn(int carNumber, string source, bool announce = true)
    {
        if (!SessionRacers.Any(r => r.Enabled && r.CarNumber == carNumber))
        {
            if (announce)
                FooterStatusText.Text = $"Car {carNumber} is not in the loaded heat.";
            return false;
        }

        var newlyCheckedIn = _checkedInCars.Add(carNumber);
        var row = Standings.FirstOrDefault(r => r.CarNumber == carNumber);
        if (row is not null)
            row.IsCheckedIn = true;

        if (newlyCheckedIn && announce)
        {
            AnnounceDriverCheckIn(carNumber);
            var racer = SessionRacers.FirstOrDefault(r => r.CarNumber == carNumber);
            FooterStatusText.Text = $"{racer?.DisplayName ?? $"Car {carNumber}"} checked in • {source}.";
            LastPassingText.Text = $"CHECK IN • CAR {carNumber} • {racer?.DisplayName ?? $"Car {carNumber}"}";
        }

        return true;
    }

    /// <summary>
    /// Common entry point for hardware decoder parsers after they resolve a
    /// transponder ID. It deliberately uses the same path as keyboard/manual
    /// input so check-in, timing, tones, and highlights cannot drift apart.
    /// </summary>
    private void RegisterResolvedTransponder(
        string transponderId,
        string source,
        DateTimeOffset? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(transponderId))
            return;

        var crossingTime = timestamp ?? DateTimeOffset.Now;

        void ResolveOnUi()
        {
            if (_practiceActive)
            {
                RegisterPracticeTransponder(transponderId, source, crossingTime);
                return;
            }

            var normalized = transponderId.Trim();
            var racer = SessionRacers.FirstOrDefault(r =>
                SplitTransponders(r.TransponderId)
                    .Any(t => string.Equals(t, normalized, StringComparison.OrdinalIgnoreCase)));

            if (racer is not null)
            {
                RegisterPassingForCar(racer.CarNumber, source, crossingTime);
                return;
            }

            // Only raise the race-assignment popup when there is an actual
            // loaded race roster. One popup collects every unknown tag.
            if (SessionRacers.Count > 0)
                QueueUnknownTransponder(normalized, source, crossingTime);
        }

        if (Dispatcher.CheckAccess())
            ResolveOnUi();
        else
            _ = Dispatcher.BeginInvoke(new Action(ResolveOnUi));
    }

    private void ApplyImmediatePassingVisual(int carNumber, PassingResult result)
    {
        var row = Standings.FirstOrDefault(r => r.CarNumber == carNumber);
        if (row is null)
            return;

        row.IsCheckedIn = true;

        if (result.Lap is null)
            return;

        if (result.Lap.Finished) row.Status = "FINISHED";
        row.Laps = result.Lap.LapNumber;
        row.LastLap = RaceEngine.FormatLap(result.Lap.LapTime);

        if (result.BestLap.HasValue)
            row.BestLap = RaceEngine.FormatLap(result.BestLap.Value);
        if (result.BestLapNumber.HasValue)
            row.BestLapNumber = result.BestLapNumber.Value.ToString();

        // This flag is driven directly from the accepted lap rather than waiting
        // for the next dashboard timer snapshot.
        row.IsNewPersonalBest = result.IsNewPersonalBest;

        if (result.IsSessionBest)
        {
            foreach (var standing in Standings)
                standing.IsFastestLap = standing.CarNumber == carNumber;
        }
    }

    private void MarkDriverPassing(int carNumber)
    {
        var now = DateTimeOffset.Now;
        _passingHighlightUntilByCar[carNumber] = now.Add(PassingHighlightDuration);

        // Multiple cars may be highlighted at the same time. Each car owns its
        // own expiration timestamp, so a later crossing does not cancel an
        // earlier racer's flash.
        StandingRow? highlighted = null;
        foreach (var row in Standings)
        {
            row.IsLastPassing = IsPassingHighlightActive(row.CarNumber, now);
            if (row.CarNumber == carNumber)
                highlighted = row;
        }

        if (highlighted is not null)
            StandingsGrid.ScrollIntoView(highlighted);

        _passingHighlightTimer ??= new DispatcherTimer(DispatcherPriority.Render)
        {
            // A short sweep interval lets each driver's independent highlight
            // expire close to its own crossing time without making the UI busy.
            Interval = TimeSpan.FromMilliseconds(100)
        };

        _passingHighlightTimer.Tick -= PassingHighlightTimer_Tick;
        _passingHighlightTimer.Tick += PassingHighlightTimer_Tick;

        if (!_passingHighlightTimer.IsEnabled)
            _passingHighlightTimer.Start();
    }

    private void MarkMinimumLapViolation(int carNumber)
    {
        var seconds = Math.Clamp(_settings.DashboardMinimumLapGlowSeconds, 0.1, 30.0);
        var now = DateTimeOffset.Now;
        _minimumLapGlowUntilByCar[carNumber] = now.AddSeconds(seconds);

        var row = Standings.FirstOrDefault(r => r.CarNumber == carNumber);
        if (row is not null)
            row.IsMinimumLapViolationGlow = true;

        _minimumLapGlowTimer ??= new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };

        _minimumLapGlowTimer.Tick -= MinimumLapGlowTimer_Tick;
        _minimumLapGlowTimer.Tick += MinimumLapGlowTimer_Tick;

        if (!_minimumLapGlowTimer.IsEnabled)
            _minimumLapGlowTimer.Start();
    }

    private bool IsMinimumLapGlowActive(int carNumber, DateTimeOffset now) =>
        _minimumLapGlowUntilByCar.TryGetValue(carNumber, out var until) && now < until;

    private void MinimumLapGlowTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.Now;
        var expired = _minimumLapGlowUntilByCar
            .Where(pair => now >= pair.Value)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var carNumber in expired)
            _minimumLapGlowUntilByCar.Remove(carNumber);

        foreach (var row in Standings)
            row.IsMinimumLapViolationGlow = IsMinimumLapGlowActive(row.CarNumber, now);

        if (_minimumLapGlowUntilByCar.Count == 0)
            _minimumLapGlowTimer?.Stop();
    }

    private bool IsPassingHighlightActive(int carNumber, DateTimeOffset now) =>
        _passingHighlightUntilByCar.TryGetValue(carNumber, out var until) && now < until;

    private void PassingHighlightTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.Now;
        var expired = _passingHighlightUntilByCar
            .Where(pair => now >= pair.Value)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var carNumber in expired)
            _passingHighlightUntilByCar.Remove(carNumber);

        var changed = expired.Count > 0;
        foreach (var row in Standings)
        {
            var active = IsPassingHighlightActive(row.CarNumber, now);
            if (row.IsLastPassing != active)
            {
                row.IsLastPassing = active;
                changed = true;
            }
        }

        if (_passingHighlightUntilByCar.Count == 0)
            _passingHighlightTimer?.Stop();
    }

    private void ClearDriverPassingHighlight()
    {
        _passingHighlightTimer?.Stop();
        _minimumLapGlowTimer?.Stop();
        _passingHighlightUntilByCar.Clear();
        _minimumLapGlowUntilByCar.Clear();

        foreach (var row in Standings)
        {
            row.IsLastPassing = false;
            row.IsMinimumLapViolationGlow = false;
        }
    }

}
