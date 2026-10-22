using System.Windows;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Dialogs;

public partial class RoundMainBuilderDialog
{
    private readonly AppSettings _speechSettings;
    private readonly List<DriverProfile> _addedDrivers = [];
    public IReadOnlyList<DriverProfile> AddedDrivers => _addedDrivers
        .Where(d => BuiltSessions.Any(s => s.Entrants.Any(e => e.DriverId == d.Id))).ToList();
    private bool HasLockedPlans => _browsingSavedLineups || HeatPlans.Any(h => h.IsLocked);

    private void ShowLockedStructureMessage() => MessageBox.Show(this,
        _browsingSavedLineups ? "Select a saved race to edit its round and class." :
        "This round contains completed results. You can add, remove, reorder, or move drivers in its unrun races. Heat structure and completed results stay locked.",
        "Saved round", MessageBoxButton.OK, MessageBoxImage.Information);

    private bool HasRoom(HeatPlan heat)
    {
        var capacity = heat.SessionType == SessionType.Race && heat.MainCapacity > 0
            ? Math.Min(30, heat.MainCapacity) : 30;
        if (heat.Entrants.Count < capacity) return true;
        MessageBox.Show(this, $"This race is full ({capacity} cars). Choose another heat/main.",
            "Race full", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    public bool CloseAfterSave { get; set; }
    public Func<RoundMainBuilderDialog, Task<bool>>? SaveBuilderRequested { get; set; }
    private void NewRound_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmLeaveLineup()) OpenCompactGenerator();
    }

    private void BeginNewRound()
    {
        _browsingSavedLineups = false;
        _lineupSnapshot = null;
        InitializeSavedFilters();
        _editingExistingScope = false;
        ClassCombo.IsEnabled = true;
        ModeCombo.IsEnabled = true;
        SortPanel.IsEnabled = true;
        GenerateButton.IsEnabled = true;
        GenerateButton.Visibility = Visibility.Visible;
        BuildOptionsPanel.Visibility = Visibility.Visible;
        SaveButton.Content = "SAVE / REBUILD";
        ApplyAutomaticNextRound();
        LoadExistingOrGenerate();
        RememberLineup();
    }

    private void AddDriver_Click(object sender, RoutedEventArgs e)
    {
        if (HeatGrid.SelectedItem is not HeatPlan heat || heat.IsLocked || _browsingSavedLineups || !HasRoom(heat)) return;
        // A qualifier runs only once per class/round. Repeated main legs may
        // contain the same driver; movement within a leg remains duplicate-free.
        var unavailable = HeatPlans.Where(h => h.ClassId == heat.ClassId &&
                (heat.SessionType != SessionType.Race || h.MainLeg == heat.MainLeg))
            .SelectMany(h => h.Entrants).Select(e => e.DriverId).ToHashSet();
        var available = _drivers.Concat(_addedDrivers).Where(d => !unavailable.Contains(d.Id))
            .OrderBy(d => d.DisplayName).ToList();
        var picker = new RaceDriverPickerDialog(available) { Owner = this };
        if (picker.ShowDialog() != true) return;
        var driver = picker.SelectedDriver;
        if (picker.CreateNewDriver)
        {
            var editor = new DriverEditorDialog(settings: _speechSettings) { Owner = this };
            if (editor.ShowDialog() != true) return;
            driver = new DriverProfile { FirstName = editor.FirstName, LastName = editor.LastName,
                Nickname = editor.Nickname, PhoneticName = editor.PhoneticName };
            driver.SyncLegacyName();
        }
        if (driver is null) return;
        var assignment = _raceEvent.DriverAssignments.FirstOrDefault(a =>
            a.DriverId == driver.Id && a.ClassId == heat.ClassId);
        var transponder = string.IsNullOrWhiteSpace(picker.Transponder)
            ? assignment?.TransponderId ?? string.Empty : picker.Transponder;
        if (!string.IsNullOrWhiteSpace(transponder) && heat.Entrants.Any(e =>
            string.Equals(e.TransponderId, transponder, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "That transponder is already assigned in this race.", "Add driver",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (picker.CreateNewDriver) _addedDrivers.Add(driver);
        var entrant = new SessionEntrant { DriverId = driver.Id, DriverName = driver.FullName,
            Nickname = driver.Nickname, TransponderId = transponder, SeedRank = assignment?.SeedRank ?? 0 };
        heat.Entrants.Add(entrant);
        Renumber(heat.Entrants);
        HeatGrid.Items.Refresh();
        EntrantGrid.SelectedItem = entrant;
        BuilderStatusText.Text = $"{entrant.DisplayName} added to {heat.Name}. Save changes to keep this lineup.";
    }

    private void RemoveDriver_Click(object sender, RoutedEventArgs e)
    {
        if (HeatGrid.SelectedItem is not HeatPlan heat || heat.IsLocked || _browsingSavedLineups ||
            EntrantGrid.SelectedItem is not SessionEntrant entrant) return;
        heat.Entrants.Remove(entrant);
        Renumber(heat.Entrants);
        HeatGrid.Items.Refresh();
        EntrantGrid.Items.Refresh();
        BuilderStatusText.Text = $"{entrant.DisplayName} removed from this race. Save changes to keep this lineup.";
    }
}
