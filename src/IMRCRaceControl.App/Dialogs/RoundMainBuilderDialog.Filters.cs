using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using IMRCRaceControl.Models;
namespace IMRCRaceControl.Dialogs;
public partial class RoundMainBuilderDialog
{
    private sealed record SavedRoundChoice(int Round, string Label); // -1 all, 0 mains
    private bool _loadingSavedFilters;
    private bool _browsingSavedLineups;
    private string? _lineupSnapshot;
    private SavedRoundChoice? _priorSavedRound;
    private ClassChoice? _priorSavedClass;
    private void RememberLineup() => _lineupSnapshot = JsonSerializer.Serialize(HeatPlans);
    private bool ConfirmLeaveLineup() => _browsingSavedLineups || _lineupSnapshot is null ||
        _lineupSnapshot == JsonSerializer.Serialize(HeatPlans) ||
        MessageBox.Show(this, "Discard unsaved lineup edits and change the view? Save first if you want to keep them.",
            "Unsaved lineup", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private void InitializeSavedFilters()
    {
        _loadingSavedFilters = true;
        var rounds = new List<SavedRoundChoice> { new(-1,"ALL ROUNDS / MAINS") };
        rounds.AddRange(_raceEvent.Sessions.Where(s=>s.SessionType==SessionType.Qualifying).Select(s=>s.Round).Distinct().Order().Select(n=>new SavedRoundChoice(n,$"Qualifying round {n}")));
        if (_raceEvent.Sessions.Any(s=>s.SessionType==SessionType.Race)) rounds.Add(new(0,"Mains"));
        SavedRoundFilter.ItemsSource = rounds; SavedRoundFilter.SelectedIndex = 0;
        SavedClassFilter.ItemsSource = ClassCombo.ItemsSource; SavedClassFilter.SelectedIndex = 0;
        _priorSavedRound = SavedRoundFilter.SelectedItem as SavedRoundChoice;
        _priorSavedClass = SavedClassFilter.SelectedItem as ClassChoice;
        _loadingSavedFilters = false;
    }
    private void SyncSavedFilters(SavedSessionRow row)
    {
        _loadingSavedFilters = true;
        SavedRoundFilter.SelectedItem = ((IEnumerable<SavedRoundChoice>)SavedRoundFilter.ItemsSource).FirstOrDefault(r=>r.Round==(row.SessionType==SessionType.Race ? 0 : row.Round));
        SavedClassFilter.SelectedItem = ((IEnumerable<ClassChoice>)SavedClassFilter.ItemsSource).FirstOrDefault(c=>c.ClassId==row.ClassId);
        _priorSavedRound = SavedRoundFilter.SelectedItem as SavedRoundChoice;
        _priorSavedClass = SavedClassFilter.SelectedItem as ClassChoice;
        _loadingSavedFilters = false;
    }
    private void SavedLineupFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSavedFilters || !IsLoaded) return;
        if (!ConfirmLeaveLineup())
        {
            _loadingSavedFilters=true; SavedRoundFilter.SelectedItem=_priorSavedRound; SavedClassFilter.SelectedItem=_priorSavedClass; _loadingSavedFilters=false; return;
        }
        _lineupSnapshot=null;
        _priorSavedRound=SavedRoundFilter.SelectedItem as SavedRoundChoice;
        _priorSavedClass=SavedClassFilter.SelectedItem as ClassChoice;
        ShowSavedLineups();
    }
    private void ShowSavedLineups()
    {
        var round=SavedRoundFilter.SelectedItem as SavedRoundChoice;
        var cls=SavedClassFilter.SelectedItem as ClassChoice;
        var sessions=_raceEvent.Sessions.Where(s=>s.SessionType is SessionType.Qualifying or SessionType.Race)
            .Where(s=>round is null || round.Round==-1 || round.Round==0 && s.SessionType==SessionType.Race || round.Round>0 && s.SessionType==SessionType.Qualifying && s.Round==round.Round)
            .Where(s=>cls is null || cls.AllClasses || s.ClassId==cls.ClassId).OrderBy(s=>s.QueueOrder).ToList();
        _browsingSavedLineups=true; _editingExistingScope=false; _lineupSnapshot=null;
        _loadingSavedFilters = true;
        try { LoadExisting(sessions); } finally { _loadingSavedFilters = false; }
        // Make all-round rows unambiguous without repeating the class in Session.
        foreach (var plan in HeatPlans.Where(h=>h.SessionType==SessionType.Qualifying)) plan.SessionDisplay=$"Q{plan.Round} • {plan.SessionDisplay}";
        HeatGrid.Items.Refresh();
        ClassCombo.IsEnabled=false; ModeCombo.IsEnabled=false; SortPanel.IsEnabled=false; GenerateButton.IsEnabled=false;
        BuildOptionsPanel.Visibility=Visibility.Collapsed; GenerateButton.Visibility=Visibility.Collapsed;
        SaveButton.IsEnabled=false; SaveButton.Content="SAVE CHANGES"; RosterEditActions.IsEnabled=false;
        BuilderStatusText.Text=$"Showing {sessions.Count} saved lineups. Select a race to edit its lineup, or choose a specific round and class above.";
        if (round is { Round: >=0 } && cls is { AllClasses:false } && sessions.Count>0)
            OpenSavedLineup(sessions[0].Id);
    }
    private void OpenSavedLineup(string id)
    {
        RefreshSavedSessions();
        SavedSessionGrid.SelectedItem=SavedSessions.FirstOrDefault(s=>s.SessionId==id);
        LoadSavedSession_Click(this,new RoutedEventArgs());
    }
    private void HeatGrid_OpenSelectedLineup(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_browsingSavedLineups && !_loadingSavedFilters &&
            e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(HeatGrid, source) is DataGridRow row &&
            row.Item is HeatPlan { SourceSessionId.Length: >0 } plan)
            OpenSavedLineup(plan.SourceSessionId);
    }
    private void EditBrowsedLineup_Click(object sender, RoutedEventArgs e)
    {
        if (HeatGrid.SelectedItem is HeatPlan { SourceSessionId.Length: >0 } plan) OpenSavedLineup(plan.SourceSessionId);
        else MessageBox.Show(this,"Select a saved race first.","Lineup");
    }
}
