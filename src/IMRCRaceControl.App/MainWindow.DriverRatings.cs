using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    public ObservableCollection<DriverRatingRow> DriverRatings { get; } = new();
    public ObservableCollection<DriverRatingRaceRow> DriverRatingHistory { get; } = new();
    private DriverRatingResult? _ratingResult;
    private bool _loadingRatings;
    private sealed record RatingClassChoice(string Id, string Name);

    private void DriverAreaTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == DriverAreaTabs && IsLoaded) RefreshDriverRatings();
    }

    private void RefreshDriverRatings()
    {
        if (RatingClassCombo is null) return;
        _loadingRatings = true;
        try
        {
            var selected = (RatingClassCombo.SelectedItem as RatingClassChoice)?.Id ?? "";
            var preferences = _settings.DriverRatingSettings;
            RatingSlowdownText.Text = preferences.IncidentSlowdownPercent.ToString(CultureInfo.InvariantCulture);
            RatingSecondsText.Text = preferences.IncidentMinimumSeconds.ToString(CultureInfo.InvariantCulture);
            RatingMinimumLapsText.Text = preferences.MinimumSafetyLaps.ToString();
            RatingPenaltyText.Text = preferences.AccidentPenalty.ToString(CultureInfo.InvariantCulture);
            RatingWindowText.Text = preferences.SafetyWindowLaps.ToString();
            _ratingResult = DriverRatingCalculator.Calculate(Events, Drivers, preferences, _settings.DriverRatingReviews);
            var choices = new List<RatingClassChoice> { new("", "All classes") };
            choices.AddRange(_ratingResult.Standings.GroupBy(r => r.ClassId).Select(g => new RatingClassChoice(g.Key, g.First().ClassName)).OrderBy(c => c.Name));
            RatingClassCombo.ItemsSource = choices;
            RatingClassCombo.SelectedItem = choices.FirstOrDefault(c => c.Id == selected) ?? choices[0];
        }
        finally { _loadingRatings = false; }
        FilterDriverRatings();
        RefreshAssignmentRows();
    }

    private void FilterDriverRatings()
    {
        if (_loadingRatings || _ratingResult is null) return;
        var selectedRow = DriverRatingsGrid.SelectedItem as DriverRatingRow;
        var selectedDriver = selectedRow?.DriverId;
        var classId = (RatingClassCombo.SelectedItem as RatingClassChoice)?.Id ?? "";
        DriverRatings.Clear();
        foreach (var row in _ratingResult.Standings.Where(r => classId.Length == 0 || r.ClassId == classId)) DriverRatings.Add(row);
        DriverRatingsGrid.SelectedItem = DriverRatings.FirstOrDefault(r => r.DriverId == selectedDriver && r.ClassId == selectedRow?.ClassId) ?? DriverRatings.FirstOrDefault();
        RatingSummaryText.Text = DriverRatings.Count == 0 ? "No eligible completed mains yet. At least two racers must record laps." : $"{DriverRatings.Count} driver/class rankings • Performance starts at 1500 • Safety starts at 3.00";
        ShowDriverRatingHistory();
    }
    private void RatingClassCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => FilterDriverRatings();
    private void DriverRatingsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDriverRatingHistory();
    private void RefreshDriverRatings_Click(object sender, RoutedEventArgs e) => RefreshDriverRatings();
    private void ShowDriverRatingHistory()
    {
        DriverRatingHistory.Clear();
        if (_ratingResult is null || DriverRatingsGrid.SelectedItem is not DriverRatingRow driver) return;
        foreach (var race in _ratingResult.History.Where(r => r.DriverId == driver.DriverId && r.ClassId == driver.ClassId)) DriverRatingHistory.Add(race);
    }
    private void DriverRatingHistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RatingExcludeSafetyCheck is null) return;
        var selected = DriverRatingHistoryGrid.SelectedItem as DriverRatingRaceRow;
        RatingExcludeSafetyCheck.IsChecked = selected?.SafetyStatus == "Excluded by review";
        RatingReviewNoteText.Text = selected?.Note ?? "";
    }
    private async void SaveRatingSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(RatingPenaltyText.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var penalty) || !double.IsFinite(penalty) || penalty < .05 || penalty > 2 ||
            !double.TryParse(RatingSlowdownText.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var slowdown) || !double.IsFinite(slowdown) || slowdown < 5 || slowdown > 200 ||
            !double.TryParse(RatingSecondsText.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds) || seconds < .1 || seconds > 60 ||
            !int.TryParse(RatingMinimumLapsText.Text, out var minimum) || minimum < 3 || minimum > 100 ||
            !int.TryParse(RatingWindowText.Text, out var window) || window < 50 || window > 2000)
        {
            MessageBox.Show(this, "Enter slowdown 5–200%, extra time 0.1–60 seconds, minimum laps 3–100 recent laps 50–2000 and accident penalty 0.05–2.00.", "Rating settings");
            return;
        }
        _settings.DriverRatingSettings = new() { IncidentSlowdownPercent = slowdown, IncidentMinimumSeconds = seconds, MinimumSafetyLaps = minimum, SafetyWindowLaps = window, AccidentPenalty = penalty };
        await CaptureAndSaveSettingsAsync();
        RefreshDriverRatings();
    }
    private async void SaveRatingReview_Click(object sender, RoutedEventArgs e)
    {
        if (DriverRatingHistoryGrid.SelectedItem is not DriverRatingRaceRow race)
        { MessageBox.Show(this, "Select a race in the driver's rating history.", "Safety review"); return; }
        if (string.IsNullOrWhiteSpace(RatingReviewNoteText.Text))
        { MessageBox.Show(this, "Enter a note explaining the safety review.", "Safety review"); return; }
        _settings.DriverRatingReviews.RemoveAll(r => r.EventId == race.EventId && r.SessionId == race.SessionId && r.DriverId == race.DriverId);
        _settings.DriverRatingReviews.Add(new()
        {
            EventId = race.EventId, SessionId = race.SessionId, DriverId = race.DriverId,
            ExcludeSafety = RatingExcludeSafetyCheck.IsChecked == true, Note = RatingReviewNoteText.Text.Trim()
        });
        await CaptureAndSaveSettingsAsync();
        RefreshDriverRatings();
    }
}
