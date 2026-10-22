using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private bool _updatingDashboardSelectors;

    private void DashboardRoundCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents || _updatingDashboardSelectors || !IsLoaded) return;
        if (DashboardRoundCombo.SelectedItem is not DashboardRoundChoice round) return;
        var selected = RaceSchedule.InitialRace(RaceSchedule.InRound(GetQueueSessions(), round));
        if (selected is null) RestoreDashboardRaceSelection();
        else SelectDashboardRace(selected);
        ReturnFocusToRaceDashboard();
    }

    private void DashboardRaceSelector_DropDownClosed(object sender, EventArgs e) => ReturnFocusToRaceDashboard();

    private void ReturnFocusToRaceDashboard()
    {
        // Run after WPF has finished closing/restoring focus from the popup.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (IsActive && MainTabs.SelectedItem == RaceTab &&
                !DashboardRoundCombo.IsDropDownOpen && !CurrentRaceCombo.IsDropDownOpen)
                Keyboard.Focus(RaceManualInputFocusTarget);
        }));
    }

    private void RefreshDashboardRaceSelectors(IReadOnlyList<EventSessionDefinition> sessions,
        EventSessionDefinition? current, bool enabled)
    {
        _updatingDashboardSelectors = true;
        try
        {
            var rounds = RaceSchedule.AvailableRounds(sessions);
            var selectedRound = current is null ? DashboardRoundCombo.SelectedItem as DashboardRoundChoice
                : DashboardRoundChoice.From(current);
            selectedRound = rounds.FirstOrDefault(r => r == selectedRound) ?? rounds.FirstOrDefault();
            DashboardRoundCombo.ItemsSource = rounds;
            DashboardRoundCombo.SelectedItem = selectedRound;
            RaceDashboardSessions.Clear();
            foreach (var session in RaceSchedule.InRound(sessions, selectedRound)) RaceDashboardSessions.Add(session);
            CurrentRaceCombo.Items.Refresh();
            CurrentRaceCombo.SelectedItem = current is not null && RaceDashboardSessions.Contains(current)
                ? current : null;
            DashboardRoundCombo.IsEnabled = enabled && rounds.Count > 0;
            CurrentRaceCombo.IsEnabled = enabled && RaceDashboardSessions.Count > 0;
        }
        finally { _updatingDashboardSelectors = false; }
    }
}
