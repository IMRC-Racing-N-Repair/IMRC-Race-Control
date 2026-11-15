using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Dialogs;

public partial class RoundMainBuilderDialog
{
    private IEnumerable<string> BuilderClassIds() => _raceEvent.ClassIds
        .Concat(_stagedLineups.Values.SelectMany(s => s).Select(s => s.ClassId))
        .Distinct(StringComparer.OrdinalIgnoreCase);

    private void HeatGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var hit = HeatGrid.InputHitTest(Mouse.GetPosition(HeatGrid));
        var row = e.CursorLeft < 0 ? HeatGrid.SelectedItem as HeatPlan : hit is null ? null : RaceAt(hit);
        ConfigureHeatContextMenu(row);
    }

    private void ConfigureHeatContextMenu(HeatPlan? row)
    {
        var addClass = row is null;
        AddClassRaceMenuItem.Visibility = addClass ? Visibility.Visible : Visibility.Collapsed;
        AddClassRaceMenuItem.IsEnabled = _raceEvent.Status == EventStatus.Active && _classes.Count > 0;
        SeparateRaceMenuItem.Visibility = RaceActionsSeparator.Visibility = AddRowHeatMenuItem.Visibility =
            RemoveRowHeatMenuItem.Visibility = addClass ? Visibility.Collapsed : Visibility.Visible;
        AddRowHeatMenuItem.IsEnabled = RemoveRowHeatMenuItem.IsEnabled = row is { IsLocked: false, IsCombinedHeader: false } && !HasLockedPlans;
        SeparateRaceMenuItem.IsEnabled = row is not null && (row.IsCombinedHeader || GroupFor(row.SourceSessionId).Length > 0);
    }

    private List<DashboardRoundChoice> PlaceholderRounds()
    {
        var filter = SavedRoundFilter.SelectedItem as SavedRoundChoice;
        var rounds = PreviewSessions.Where(s => s.SessionType is SessionType.Qualifying or SessionType.Race)
            .Select(DashboardRoundChoice.From).Distinct()
            .OrderBy(r => r.Type == SessionType.Qualifying ? 0 : 1).ThenBy(r => r.Number).ToList();
        if (rounds.Count == 0)
            rounds.AddRange([new(SessionType.Qualifying, 1), new(SessionType.Race, 1)]);
        return rounds.Where(r => filter is null or { Round: -1 } ||
            filter.Round == 0 && r.Type == SessionType.Race && (filter.MainLeg is null || r.Number == filter.MainLeg) ||
            filter.Round > 0 && r.Type == SessionType.Qualifying && r.Number == filter.Round).ToList();
    }

    private void AddClassRace_Click(object sender, RoutedEventArgs e)
    {
        if (_raceEvent.Status != EventStatus.Active) return;
        var selected = HeatGrid.SelectedItem as HeatPlan;
        StageCurrentLineup();
        var rounds = PlaceholderRounds();
        if (rounds.Count == 0) return;

        var window = new Window { Owner = this, Title = "Add race for another class", Width = 490,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20) };
        window.Content = panel;
        panel.Children.Add(new TextBlock { Text = "RESERVE A RACE SPOT", FontSize = 19, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = "Choose a class that isn’t scheduled in this round. The new race starts with 0 racers.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 12)
        });
        var roundPicker = new ComboBox { ItemsSource = rounds, DisplayMemberPath = nameof(DashboardRoundChoice.Label) };
        var classPicker = new ComboBox { DisplayMemberPath = nameof(ClassDefinition.Name), IsTextSearchEnabled = true };
        panel.Children.Add(new Label { Content = "_Round / mains", Target = roundPicker });
        panel.Children.Add(roundPicker);
        panel.Children.Add(new Label { Content = "_Class", Target = classPicker });
        panel.Children.Add(classPicker);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(new Button { Content = "CANCEL", IsCancel = true });
        var add = new Button { Content = "ADD EMPTY RACE", IsDefault = true, IsEnabled = false };
        buttons.Children.Add(add);
        panel.Children.Add(buttons);
        void UpdateClasses()
        {
            var available = roundPicker.SelectedItem is DashboardRoundChoice round
                ? SavedRoundEditor.AvailablePlaceholderClasses(_classes, PreviewSessions, round) : [];
            classPicker.ItemsSource = available;
            classPicker.SelectedIndex = available.Count == 1 ? 0 : -1;
            status.Text = available.Count == 0 ? "Every configured class already has a race in this round." : "";
        }
        roundPicker.SelectionChanged += (_, _) => UpdateClasses();
        classPicker.SelectionChanged += (_, _) => add.IsEnabled = classPicker.SelectedItem is ClassDefinition;
        add.Click += (_, _) =>
        {
            if (roundPicker.SelectedItem is DashboardRoundChoice && classPicker.SelectedItem is ClassDefinition)
                window.DialogResult = true;
        };
        var preferred = selected is null ? null : new DashboardRoundChoice(selected.SessionType,
            selected.SessionType == SessionType.Race ? selected.MainLeg : selected.Round);
        roundPicker.SelectedItem = rounds.FirstOrDefault(r => r == preferred) ?? rounds[0];
        if (window.ShowDialog() != true || roundPicker.SelectedItem is not DashboardRoundChoice round ||
            classPicker.SelectedItem is not ClassDefinition cls) return;
        try { StageClassRace(cls, round); }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Add empty race", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private EventSessionDefinition StageClassRace(ClassDefinition cls, DashboardRoundChoice round)
    {
        if (_raceEvent.Status != EventStatus.Active)
            throw new InvalidOperationException("Completed events are read-only.");
        StageCurrentLineup();
        var sessions = PreviewSessions;
        var race = SavedRoundEditor.CreatePlaceholder(cls, round, sessions);
        var scope = LineupScope.For(race);
        _stagedLineups[scope] = sessions.Where(s => LineupScope.For(s) == scope).Append(race).ToList();

        // Keep the new slot at the end of its round, including after the save
        // path rebuilds the queue from class order.
        var order = 0;
        foreach (var existing in sessions.Where(s => round.Matches(s) && RaceLineupOrder.Movable(s))
            .OrderBy(s => _pendingRaceOrder.GetValueOrDefault(s.Id, s.LineupOrder ?? int.MaxValue)).ThenBy(s => s.QueueOrder))
            _pendingRaceOrder[existing.Id] = ++order;
        _pendingRaceOrder[race.Id] = ++order;

        RefreshPlaceholderClassChoices(cls);
        OpenSavedLineup(race.Id);
        BuilderStatusText.Text = $"{cls.Name} added to {round.Label} with 0 racers. Add drivers when ready and save changes to keep the spot.";
        return race;
    }

    private void RefreshPlaceholderClassChoices(ClassDefinition? added = null)
    {
        var selected = ClassCombo.SelectedItem as ClassChoice;
        var filter = SavedClassFilter.SelectedItem as ClassChoice;
        var choices = new List<ClassChoice> { new("", "ALL EVENT CLASSES", true) };
        choices.AddRange(BuilderClassIds().Select(id => _classes.FirstOrDefault(c => c.Id == id))
            .OfType<ClassDefinition>().Select(c => new ClassChoice(c.Id, c.Name, false)));
        _loadingOptions = _loadingSavedFilters = true;
        try
        {
            ClassCombo.ItemsSource = SavedClassFilter.ItemsSource = choices;
            ClassCombo.SelectedItem = choices.FirstOrDefault(c => c.ClassId == selected?.ClassId) ?? choices[0];
            // A different class filter would hide the new placeholder.
            var keepFilter = added is null || filter?.ClassId == added.Id;
            SavedClassFilter.SelectedItem = (keepFilter ? choices.FirstOrDefault(c => c.ClassId == filter?.ClassId) : null) ?? choices[0];
            _priorSavedClass = SavedClassFilter.SelectedItem as ClassChoice;
        }
        finally { _loadingOptions = _loadingSavedFilters = false; }
    }
}
