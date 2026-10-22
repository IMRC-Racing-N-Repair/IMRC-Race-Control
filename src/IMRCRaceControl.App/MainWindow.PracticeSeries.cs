using IMRCRaceControl.Core;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private readonly PracticeHistoryService _practiceHistoryService = new();
    private readonly DispatcherTimer _practiceTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(500)
    };

    private bool _practiceActive;
    private bool _practiceTimerBusy;
    private RaceSeriesDefinition? _activeSeries;

    public ObservableCollection<PracticeCardViewModel> PracticeCards { get; } = new();
    public ObservableCollection<PracticeCardViewModel> VisiblePracticeCards { get; } = new();
    public ObservableCollection<PracticeHistoryRow> PracticeHistory { get; } = new();
    public ObservableCollection<PracticeHistoryLapRow> PracticeHistoryLaps { get; } = new();

    public ObservableCollection<RaceSeriesDefinition> SeriesDefinitions { get; } = new();
    public ObservableCollection<SeriesEventChoice> SeriesEventChoices { get; } = new();
    public ObservableCollection<SeriesClassChoice> SeriesClassChoices { get; } = new();
    public ObservableCollection<SeriesStandingRow> SeriesStandings { get; } = new();

    private void InitializePracticeSeriesInfrastructure()
    {
        _practiceTimer.Tick += PracticeTimer_Tick;
    }

    private async Task InitializePracticeDatabaseAsync()
    {
        try
        {
            await _practiceHistoryService.InitializeAsync();
        }
        catch (Exception ex)
        {
            FooterStatusText.Text = $"Practice database warning: {ex.Message}";
        }
    }

    private void ApplyPracticeStatPreferences()
    {
        foreach (var card in PracticeCards)
            card.SetVisibleStats(_settings.PracticeVisibleStats);
    }

    private const int MaximumVisiblePracticeCards = 36;

    private void ApplyPracticeLayoutPreferences()
    {
        PracticeGridColumns = Math.Clamp(_settings.PracticeCardsPerRow, 4, 6);
        RefreshVisiblePracticeCards();
    }

    private void RefreshVisiblePracticeCards()
    {
        var desired = PracticeCards
            .Take(MaximumVisiblePracticeCards)
            .ToList();

        // Avoid rebuilding the card visuals when the visible set has not changed.
        var same =
            VisiblePracticeCards.Count == desired.Count &&
            VisiblePracticeCards.Select(c => c.StintId)
                .SequenceEqual(desired.Select(c => c.StintId), StringComparer.OrdinalIgnoreCase);

        if (!same)
        {
            VisiblePracticeCards.Clear();
            foreach (var card in desired)
                VisiblePracticeCards.Add(card);
        }

        var visibleCount = Math.Min(PracticeCards.Count, MaximumVisiblePracticeCards);
        PracticeGridRows = Math.Max(
            2,
            (int)Math.Ceiling(Math.Max(1, visibleCount) / (double)Math.Max(1, PracticeGridColumns)));

        if (PracticeLayoutStatusText is not null)
        {
            PracticeLayoutStatusText.Text =
                PracticeCards.Count > MaximumVisiblePracticeCards
                    ? $"Showing {MaximumVisiblePracticeCards} of {PracticeCards.Count} active racers • {PracticeGridColumns} per row"
                    : $"{PracticeCards.Count} active racer{(PracticeCards.Count == 1 ? string.Empty : "s")} • {PracticeGridColumns} per row • {PracticeGridRows} row{(PracticeGridRows == 1 ? string.Empty : "s")}";
        }
    }

    // -----------------------------------------------------------------
    // Practice
    // -----------------------------------------------------------------

    private async void PracticeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_practiceActive)
        {
            await StopPracticeAsync();
            return;
        }

        if (_resultAutoSaveInProgress)
        {
            FooterStatusText.Text = "Wait for the completed race result to finish saving before enabling Practice.";
            return;
        }

        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            MessageBox.Show(this,
                "End, false-start, or restart the current race before enabling Practice.",
                "Practice",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _lapSpeech.Stop();
        _practiceActive = true;
        PracticeToggleButton.Content = "STOP PRACTICE";
        PracticeToggleButton.SetResourceReference(Control.BackgroundProperty, "AccentDarkBrush");
        PracticeToggleButton.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        PracticeManualDriverCombo.IsEnabled = true;
        PracticeManualPassButton.IsEnabled = true;
        PracticeStatusText.Text = "PRACTICE ACTIVE • race sessions locked";
        HeaderRaceStateText.Text = "PRACTICE";
        HeaderMessageText.Text = "Practice active • waiting for transponder crossings.";
        ApplyPracticeLayoutPreferences();
        _practiceTimer.Start();

        UpdateRaceDashboardAvailability();
        FooterStatusText.Text = "Practice started. Driver cards appear on their first crossing.";
    }

    private async Task StopPracticeAsync()
    {
        // Close ingress before awaiting storage so no crossings/callouts arrive during stop.
        _practiceActive = false;
        _lapSpeech.Stop();
        _practiceTimer.Stop();

        foreach (var card in PracticeCards.ToList())
            await FinalizePracticeCardAsync(card, DateTimeOffset.Now);

        _practiceActive = false;
        PracticeToggleButton.Content = "START PRACTICE";
        PracticeToggleButton.SetResourceReference(Control.BackgroundProperty, "GoodBrush");
        PracticeToggleButton.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(16, 19, 24));
        PracticeManualDriverCombo.IsEnabled = false;
        PracticeManualPassButton.IsEnabled = false;
        PracticeStatusText.Text = "Practice is off";

        UpdateRaceDashboardAvailability();
        HeaderRaceStateText.Text = _activeEvent is null ? "NO EVENT" : "READY";
        HeaderMessageText.Text = _activeEvent is null
            ? "Create an event and build a race-day schedule."
            : $"Event ready: {_activeEvent.Name}";

        await RefreshPracticeHistoryAsync();
        FooterStatusText.Text = "Practice stopped. Active practice stints were saved locally.";
    }

    private void PracticeManualPass_Click(object sender, RoutedEventArgs e)
    {
        if (!_practiceActive)
            return;

        if (PracticeManualDriverCombo.SelectedItem is not DriverProfile driver)
        {
            FooterStatusText.Text = "Select a driver for manual practice simulation.";
            return;
        }

        var assignment = _driverAssignments.FirstOrDefault(a => a.DriverId == driver.Id);
        RegisterPracticeCrossing(
            driver,
            assignment?.TransponderId ?? "MANUAL",
            "Manual Practice",
            DateTimeOffset.Now);
    }

    private void RegisterPracticeTransponder(string transponderId, string source, DateTimeOffset timestamp)
    {
        if (!_practiceActive || string.IsNullOrWhiteSpace(transponderId))
            return;

        var normalized = transponderId.Trim();
        var assignment = _driverAssignments.FirstOrDefault(a =>
            SplitTransponders(a.TransponderId)
                .Any(t => string.Equals(t, normalized, StringComparison.OrdinalIgnoreCase)));

        var driver = assignment is null ? null : Drivers.FirstOrDefault(d => d.Id == assignment.DriverId);
        RegisterPracticeCrossing(driver, normalized, source, timestamp);
    }

    private void RegisterPracticeCrossing(
        DriverProfile? driver, string transponderId, string source, DateTimeOffset timestamp)
    {
        if (!_practiceActive) return;
        var crossing = PracticeTracker.Record(PracticeCards, driver, transponderId, source, timestamp);
        var card = crossing.Card;
        if (crossing.Created || crossing.LapAdded) card.IsLastPassing = true;
        if (crossing.Created)
        {
            card.SetVisibleStats(_settings.PracticeVisibleStats);
            RefreshVisiblePracticeCards();
        }
        if (crossing.DriverResolved && driver is not null) AnnounceCheckInName(driver.SpokenName);
        if (crossing.LapAdded && _settings.PracticeLapCalloutsEnabled)
            AnnounceLapTime(card.Laps[^1].LapTime);
        FooterStatusText.Text = crossing.Created
            ? $"{card.DriverName} • Transponder {transponderId} entered Practice."
            : $"{card.DriverName} • Practice Lap {card.LapCount} • {card.LastLapDisplay}";
    }

    private async void PracticeTimer_Tick(object? sender, EventArgs e)
    {
        if (!_practiceActive || _practiceTimerBusy)
            return;

        _practiceTimerBusy = true;
        try
        {
            var now = DateTimeOffset.Now;
            foreach (var card in PracticeCards)
            {
                card.IsLastPassing = now - card.LastCrossingAt < PassingHighlightDuration;
                card.SetVisibleStats(_settings.PracticeVisibleStats);
            }

            var inactive = PracticeCards
                .Where(card => !card.IsSaving && now - card.LastCrossingAt >= card.InactivityTimeout)
                .ToList();

            foreach (var card in inactive)
                await FinalizePracticeCardAsync(card, now);
        }
        finally
        {
            _practiceTimerBusy = false;
        }
    }

    private async Task FinalizePracticeCardAsync(PracticeCardViewModel card, DateTimeOffset endedAt)
    {
        if (card.IsSaving)
            return;

        card.IsSaving = true;
        try
        {
            await _practiceHistoryService.SaveStintAsync(card, endedAt);
        }
        catch (Exception ex)
        {
            FooterStatusText.Text = $"Practice save warning: {ex.Message}";
            card.IsSaving = false;
            return;
        }

        PracticeCards.Remove(card);
        RefreshVisiblePracticeCards();
    }

    private async Task FinalizeAllPracticeOnCloseAsync()
    {
        _practiceTimer.Stop();
        _practiceActive = false;

        foreach (var card in PracticeCards.ToList())
            await FinalizePracticeCardAsync(card, DateTimeOffset.Now);
    }

    private async void PracticeViewTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != PracticeViewTabs || !IsLoaded)
            return;

        if (PracticeViewTabs.SelectedIndex == 1)
            await RefreshPracticeHistoryAsync();
    }

    private async Task RefreshPracticeHistoryAsync()
    {
        try
        {
            var rows = await _practiceHistoryService.LoadRecentStintsAsync();

            PracticeHistory.Clear();
            foreach (var row in rows)
                PracticeHistory.Add(row);

            PracticeHistoryLaps.Clear();
            PracticeHistoryLapTitle.Text = PracticeHistory.Count == 0
                ? "NO SAVED PRACTICE YET"
                : "SELECT A PRACTICE STINT";
        }
        catch (Exception ex)
        {
            FooterStatusText.Text = $"Practice history warning: {ex.Message}";
        }
    }

    private async void PracticeHistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PracticeHistoryGrid.SelectedItem is not PracticeHistoryRow selected)
            return;

        try
        {
            var laps = await _practiceHistoryService.LoadLapsAsync(selected.StintId);
            PracticeHistoryLaps.Clear();
            foreach (var lap in laps)
                PracticeHistoryLaps.Add(lap);

            PracticeHistoryLapTitle.Text =
                $"{selected.DriverName.ToUpperInvariant()} • {selected.DateDisplay} • {selected.LapCount} LAPS";
        }
        catch (Exception ex)
        {
            FooterStatusText.Text = $"Practice lap history warning: {ex.Message}";
        }
    }

    // -----------------------------------------------------------------
    // Series
    // -----------------------------------------------------------------

}
