using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private string _eventRosterClassId = string.Empty;

    private void RefreshEventSetupCollections()
    {
        RefreshEventClassRows(_eventRosterClassId);
        RefreshEventDriverChoices();
    }

    private void RefreshEventClassRows(string? selectedClassId = null)
    {
        selectedClassId = string.IsNullOrWhiteSpace(selectedClassId)
            ? _eventRosterClassId
            : selectedClassId;

        EventSelectedClasses.Clear();
        if (_activeEvent is not null)
        {
            for (var i = 0; i < _activeEvent.ClassIds.Count; i++)
            {
                var cls = Classes.FirstOrDefault(c => c.Id == _activeEvent.ClassIds[i]);
                if (cls is null)
                    continue;

                EventSelectedClasses.Add(new EventClassRow
                {
                    Order = i + 1,
                    ClassId = cls.Id,
                    ClassName = cls.Name,
                    RacersPerHeat = cls.MaxRacersPerHeat
                });
            }
        }

        EventClassChoices.Clear();
        var includedOrder = _activeEvent?.ClassIds
            .Select((id, index) => new { id, order = index + 1 })
            .ToDictionary(x => x.id, x => x.order, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var eventEditable = _activeEvent?.Status == EventStatus.Active;

        foreach (var cls in Classes
                     .OrderBy(c => includedOrder.TryGetValue(c.Id, out var order) ? 0 : 1)
                     .ThenBy(c => includedOrder.TryGetValue(c.Id, out var order) ? order : int.MaxValue)
                     .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            EventClassChoices.Add(new EventClassChoice
            {
                ClassId = cls.Id,
                ClassName = cls.Name,
                RacersPerHeat = cls.MaxRacersPerHeat,
                Included = includedOrder.ContainsKey(cls.Id),
                Order = includedOrder.TryGetValue(cls.Id, out var order) ? order : 0,
                IsEditable = eventEditable
            });
        }

        if (_activeEvent is null || _activeEvent.ClassIds.Count == 0)
        {
            _eventRosterClassId = string.Empty;
            _suppressUiEvents = true;
            try
            {
                EventClassChecklist.SelectedItem = null;
                EventRosterClassCombo.SelectedItem = null;
            }
            finally
            {
                _suppressUiEvents = false;
            }
            RefreshEventDriverChoices();
            return;
        }

        if (string.IsNullOrWhiteSpace(selectedClassId) || !_activeEvent.ClassIds.Contains(selectedClassId, StringComparer.OrdinalIgnoreCase))
            selectedClassId = _activeEvent.ClassIds[0];

        _eventRosterClassId = selectedClassId;

        _suppressUiEvents = true;
        try
        {
            EventClassChecklist.SelectedItem =
                EventClassChoices.FirstOrDefault(c => c.ClassId == selectedClassId);
            EventRosterClassCombo.SelectedItem =
                EventSelectedClasses.FirstOrDefault(c => c.ClassId == selectedClassId);
        }
        finally
        {
            _suppressUiEvents = false;
        }

        RefreshEventDriverChoices();
    }

    private void RefreshEventDriverChoices(string? selectDriverId = null)
    {
        RefreshEventRegistrationView();
        selectDriverId ??= (EventDriverChecklist?.SelectedItem as EventDriverChoice)?.DriverId;
        EventDriverChoices.Clear();

        SelectedEventDriverText.Text = "Select a checked driver";
        SelectedEventDriverFullNameText.Text = "—";
        EventDriverTransponderText.Text = string.Empty;

        if (_activeEvent is null || string.IsNullOrWhiteSpace(_eventRosterClassId))
        {
            EventRosterClassTitleText.Text = "DRIVERS • SELECT A CHECKED CLASS";
            return;
        }

        var cls = Classes.FirstOrDefault(c => c.Id == _eventRosterClassId);
        if (cls is null)
            return;

        EventRosterClassTitleText.Text = $"DRIVERS • {cls.Name.ToUpperInvariant()}";
        var query = EventDriverSearchText?.Text ?? string.Empty;

        var assignments = _activeEvent.DriverAssignments
            .Where(a => a.ClassId == _eventRosterClassId)
            .GroupBy(a => a.DriverId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var choices = Drivers.Select(driver =>
        {
            assignments.TryGetValue(driver.Id, out var assignment);
            return new EventDriverChoice
            {
                DriverId = driver.Id,
                DriverName = driver.FullName,
                FirstName = driver.FirstName,
                LastName = driver.LastName,
                Nickname = driver.Nickname,
                AssignmentId = assignment?.Id ?? string.Empty,
                Included = assignment is not null,
                TransponderId = assignment?.TransponderId ?? string.Empty,
                IsEditable = _activeEvent.Status == EventStatus.Active
            };
        })
        .Where(choice => choice.Matches(query))
        .OrderByDescending(choice => choice.Included)
        .ThenBy(choice => choice.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToList();

        foreach (var choice in choices)
            EventDriverChoices.Add(choice);

        if (!string.IsNullOrWhiteSpace(selectDriverId))
        {
            var selected = EventDriverChoices.FirstOrDefault(c => c.DriverId == selectDriverId);
            if (selected is not null && EventDriverChecklist is not null)
                EventDriverChecklist.SelectedItem = selected;
        }
    }

    private async void EventNameEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_activeEvent?.Status != EventStatus.Active)
            return;

        SaveEventEditorToModel();
        EventSelectCombo.Items.Refresh();
        RaceEventText.Text = _activeEvent.Name;
        await CaptureAndSaveSettingsAsync();
    }

    private async void EventDateTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_activeEvent?.Status != EventStatus.Active)
            return;

        if (!TryParseEventDate(EventDateTextBox.Text, out var parsed))
        {
            MessageBox.Show(this, "Enter the event date as MM/DD/YYYY.", "Event date",
                MessageBoxButton.OK, MessageBoxImage.Information);
            EventDateTextBox.Text = _activeEvent.EventDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
            return;
        }

        _activeEvent.EventDate = parsed;
        EventDateTextBox.Text = parsed.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
        EventSelectCombo.Items.Refresh();
        await CaptureAndSaveSettingsAsync();
    }

    private static bool TryParseEventDate(string? value, out DateTime date)
    {
        var formats = new[] { "M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd" };
        return DateTime.TryParseExact(
            (value ?? string.Empty).Trim(), formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);
    }

    private async void EventNewClass_Click(object sender, RoutedEventArgs e)
    {
        if (_activeEvent?.Status != EventStatus.Active)
            return;

        var cls = ShowClassEditor();
        if (cls is null)
            return;

        if (!_activeEvent.ClassIds.Contains(cls.Id, StringComparer.OrdinalIgnoreCase))
            _activeEvent.ClassIds.Add(cls.Id);

        _eventRosterClassId = cls.Id;
        RefreshEventClassRows(cls.Id);
        RebuildRemainingQueueFromClassOrder();
        RefreshPlannedSessions();
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"Created {cls.Name} and added it to this event.";
    }

    private async void EventClassCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_activeEvent?.Status != EventStatus.Active ||
            sender is not CheckBox check ||
            check.Tag is not string classId)
            return;

        var cls = Classes.FirstOrDefault(c => c.Id == classId);
        if (cls is null)
            return;

        var wantsIncluded = check.IsChecked == true;
        var alreadyIncluded = _activeEvent.ClassIds.Contains(classId, StringComparer.OrdinalIgnoreCase);

        if (wantsIncluded && !alreadyIncluded)
        {
            _activeEvent.ClassIds.Add(classId);
            _eventRosterClassId = classId;
            FooterStatusText.Text = $"Added {cls.Name} to the event.";
        }
        else if (!wantsIncluded && alreadyIncluded)
        {
            if (_activeEvent.Sessions.Any(s => s.ClassId == classId && s.ProgressStatus == EventSessionStatus.Completed))
            {
                MessageBox.Show(this, "That class already has completed results and must remain in the event history.",
                    "Class is in event history", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshEventClassRows(classId);
                return;
            }

            var pending = _activeEvent.Sessions
                .Where(s => s.ClassId == classId && s.ProgressStatus != EventSessionStatus.Completed)
                .ToList();

            if (pending.Count > 0 && MessageBox.Show(this,
                    $"Unchecking {cls.Name} will also remove {pending.Count} uncompleted heat(s) and its event roster. Continue?",
                    "Remove class", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                RefreshEventClassRows(classId);
                return;
            }

            foreach (var session in pending)
                _activeEvent.Sessions.Remove(session);
            _activeEvent.DriverAssignments.RemoveAll(a => a.ClassId == classId);
            _activeEvent.ClassIds.RemoveAll(id => string.Equals(id, classId, StringComparison.OrdinalIgnoreCase));

            _eventRosterClassId = _activeEvent.ClassIds.FirstOrDefault() ?? string.Empty;
            FooterStatusText.Text = $"Removed {cls.Name} from the event.";
        }

        RefreshEventClassRows(_eventRosterClassId);
        RebuildRemainingQueueFromClassOrder();
        RefreshPlannedSessions();
        await CaptureAndSaveSettingsAsync();
    }

    private void EventClassChecklist_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents || EventClassChecklist.SelectedItem is not EventClassChoice choice || !choice.Included)
            return;

        _eventRosterClassId = choice.ClassId;
        RefreshEventDriverChoices();
    }

    private async void MoveEventClassUp_Click(object sender, RoutedEventArgs e) => await MoveSelectedEventClassAsync(-1);
    private async void MoveEventClassDown_Click(object sender, RoutedEventArgs e) => await MoveSelectedEventClassAsync(1);

    private async Task MoveSelectedEventClassAsync(int offset)
    {
        if (_activeEvent?.Status != EventStatus.Active || EventClassChecklist.SelectedItem is not EventClassChoice choice || !choice.Included)
            return;

        var index = _activeEvent.ClassIds.FindIndex(id => string.Equals(id, choice.ClassId, StringComparison.OrdinalIgnoreCase));
        var target = index + offset;
        if (index < 0 || target < 0 || target >= _activeEvent.ClassIds.Count)
            return;

        (_activeEvent.ClassIds[index], _activeEvent.ClassIds[target]) = (_activeEvent.ClassIds[target], _activeEvent.ClassIds[index]);
        _eventRosterClassId = choice.ClassId;
        RefreshEventClassRows(choice.ClassId);
        RebuildRemainingQueueFromClassOrder();
        RefreshPlannedSessions();
        await CaptureAndSaveSettingsAsync();
    }

    private void EventDriverSearchText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressUiEvents)
            return;
        RefreshEventDriverChoices();
    }

    private async void EventNewDriver_Click(object sender, RoutedEventArgs e)
    {
        if (_activeEvent?.Status != EventStatus.Active)
            return;

        if (string.IsNullOrWhiteSpace(_eventRosterClassId))
        {
            MessageBox.Show(this, "Check and select an event class before adding a driver.",
                "Event roster", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var driver = ShowDriverEditor();
        if (driver is null)
            return;

        if (!await PrepareRosterChangeAsync(_eventRosterClassId))
        {
            // The driver profile is still valid and remains in the directory even if
            // the race director decides not to rebuild this class's planned heats.
            await CaptureAndSaveSettingsAsync();
            RefreshEventDriverChoices(driver.Id);
            return;
        }

        var item = new EventDriverAssignment
        {
            DriverId = driver.Id,
            ClassId = _eventRosterClassId,
            TransponderId = string.Empty,
            SeedRank = 0
        };
        _activeEvent.DriverAssignments.Add(item);

        _suppressUiEvents = true;
        try { EventDriverSearchText.Clear(); }
        finally { _suppressUiEvents = false; }
        RefreshEventDriverChoices(driver.Id);
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"Created {driver.DisplayName} and added them to the selected event class.";
    }

    private async void EventDriverCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_activeEvent?.Status != EventStatus.Active ||
            string.IsNullOrWhiteSpace(_eventRosterClassId) ||
            sender is not CheckBox check ||
            check.Tag is not string driverId)
            return;

        var driver = Drivers.FirstOrDefault(d => d.Id == driverId);
        if (driver is null)
            return;

        var existing = _activeEvent.DriverAssignments.FirstOrDefault(a =>
            a.ClassId == _eventRosterClassId && a.DriverId == driverId);
        var wantsIncluded = check.IsChecked == true;

        if (wantsIncluded && existing is null)
        {
            if (!await PrepareRosterChangeAsync(_eventRosterClassId))
            {
                RefreshEventDriverChoices(driverId);
                return;
            }

            var defaults = _driverAssignments
                .Where(a => a.DriverId == driverId && a.ClassId == _eventRosterClassId)
                .ToList();
            var transponders = defaults.Select(a => (a.TransponderId ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var ranks = defaults.Select(a => a.SeedRank).Where(r => r > 0).ToArray();

            _activeEvent.DriverAssignments.Add(new EventDriverAssignment
            {
                DriverId = driverId,
                ClassId = _eventRosterClassId,
                TransponderId = string.Join(";", transponders),
                SeedRank = ranks.Length == 0 ? 0 : ranks.Min()
            });
            FooterStatusText.Text = $"Added {driver.DisplayName} to this event class.";
        }
        else if (!wantsIncluded && existing is not null)
        {
            if (!await PrepareRosterChangeAsync(_eventRosterClassId))
            {
                RefreshEventDriverChoices(driverId);
                return;
            }

            _activeEvent.DriverAssignments.Remove(existing);
            FooterStatusText.Text = $"Removed {driver.DisplayName} from this event class.";
        }

        RefreshEventDriverChoices(driverId);
        await CaptureAndSaveSettingsAsync();
    }

    private void EventDriverChecklist_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EventDriverChecklist.SelectedItem is not EventDriverChoice choice)
        {
            SelectedEventDriverText.Text = "Select a checked driver";
            SelectedEventDriverFullNameText.Text = "—";
            EventDriverTransponderText.Text = string.Empty;
            return;
        }

        SelectedEventDriverText.Text = choice.DisplayName;
        SelectedEventDriverFullNameText.Text = choice.FullName;
        EventDriverTransponderText.Text = choice.Included ? choice.TransponderId : string.Empty;
    }

    private async void UpdateEventDriver_Click(object sender, RoutedEventArgs e)
    {
        if (_activeEvent?.Status != EventStatus.Active ||
            EventDriverChecklist.SelectedItem is not EventDriverChoice choice ||
            !choice.Included)
        {
            FooterStatusText.Text = "Check and select a driver before editing the event transponder.";
            return;
        }

        var assignment = _activeEvent.DriverAssignments.FirstOrDefault(a =>
            a.ClassId == _eventRosterClassId && a.DriverId == choice.DriverId);
        if (assignment is null)
            return;

        assignment.TransponderId = EventDriverTransponderText.Text.Trim();
        UpdateFutureSessionTransponder(assignment);
        RefreshEventDriverChoices(choice.DriverId);
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = string.IsNullOrWhiteSpace(assignment.TransponderId)
            ? $"{choice.DisplayName} will run without a saved transponder for this class."
            : $"Updated {choice.DisplayName}'s event transponder to {assignment.TransponderId}.";
    }

    private Task<bool> PrepareRosterChangeAsync(string classId)
    {
        if (_activeEvent is null) return Task.FromResult(false);
        if (_activeEvent.Sessions.Any(s => s.ClassId == classId))
            ShowRegistrationLineupWarning();
        return Task.FromResult(true);
    }

    private void UpdateFutureSessionTransponder(EventDriverAssignment assignment)
    {
        if (_activeEvent is null)
            return;

        foreach (var session in _activeEvent.Sessions.Where(s =>
                     s.ClassId == assignment.ClassId &&
                     s.ProgressStatus is EventSessionStatus.Planned or EventSessionStatus.Loaded))
        {
            foreach (var entrant in session.Entrants.Where(e => e.DriverId == assignment.DriverId))
                entrant.TransponderId = assignment.TransponderId;
        }
    }

    private void RebuildRemainingQueueFromClassOrder()
    {
        if (_activeEvent is null)
            return;

        var completed = _activeEvent.Sessions
            .Where(s => s.ProgressStatus is EventSessionStatus.Completed or EventSessionStatus.Skipped)
            .OrderBy(s => s.QueueOrder)
            .ToList();
        var remaining = _activeEvent.Sessions
            .Where(s => s.ProgressStatus is not EventSessionStatus.Completed and not EventSessionStatus.Skipped)
            .ToList();

        var classOrder = _activeEvent.ClassIds
            .Select((id, index) => new { id, index })
            .ToDictionary(x => x.id, x => x.index, StringComparer.OrdinalIgnoreCase);

        remaining = remaining
            .OrderBy(s => s.SessionType == SessionType.Qualifying ? 0 : 1)
            .ThenBy(s => s.SessionType == SessionType.Race ? -s.Heat : s.Round)
            .ThenBy(s => classOrder.TryGetValue(s.ClassId, out var index) ? index : int.MaxValue)
            .ThenBy(s => s.SessionType == SessionType.Race ? s.MainLeg : s.Heat)
            .ToList();

        var order = 1;
        foreach (var session in completed)
            session.QueueOrder = order++;
        foreach (var session in remaining)
            session.QueueOrder = order++;
    }

    private async void MoveSelectedHeatUp_Click(object sender, RoutedEventArgs e) => await MoveSelectedHeatQueueAsync(-1);
    private async void MoveSelectedHeatDown_Click(object sender, RoutedEventArgs e) => await MoveSelectedHeatQueueAsync(1);

    private async Task MoveSelectedHeatQueueAsync(int offset)
    {
        if (_activeEvent?.Status != EventStatus.Active || PlannedSessionGrid.SelectedItem is not EventSessionDefinition selected)
            return;
        if (selected.ProgressStatus is EventSessionStatus.Completed or EventSessionStatus.Skipped)
            return;

        var movable = _activeEvent.Sessions
            .Where(s => s.ProgressStatus is not EventSessionStatus.Completed and not EventSessionStatus.Skipped)
            .OrderBy(s => s.QueueOrder)
            .ToList();
        var index = movable.IndexOf(selected);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= movable.Count)
            return;

        (movable[index], movable[target]) = (movable[target], movable[index]);
        ApplyRemainingQueueOrder(movable);
        RefreshPlannedSessions(selected.Id);
        await CaptureAndSaveSettingsAsync();
    }

    private void ApplyRemainingQueueOrder(IReadOnlyList<EventSessionDefinition> orderedRemaining)
    {
        if (_activeEvent is null)
            return;

        var completed = _activeEvent.Sessions
            .Where(s => s.ProgressStatus is EventSessionStatus.Completed or EventSessionStatus.Skipped)
            .OrderBy(s => s.QueueOrder)
            .ToList();
        var order = 1;
        foreach (var session in completed)
            session.QueueOrder = order++;
        foreach (var session in orderedRemaining)
            session.QueueOrder = order++;
    }
}
