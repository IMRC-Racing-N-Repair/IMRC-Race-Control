using System.Text.Json;
using System.Windows;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;
public partial class MainWindow
{
    private void RefreshRentalContext()
    {
        _settings.Events = Events.ToList(); _settings.Drivers = Drivers.ToList(); _settings.Classes = Classes.ToList();
        RentalView.Configure(_settings, DetectedTransponders, Events, ExecuteRentalChangeAsync, _activeEvent?.Id);
    }
    private async Task ExecuteRentalChangeAsync(Action mutation, bool changesAssignments)
    {
        if (changesAssignments && !ApplicationUpdateAllowed)
            throw new InvalidOperationException("Finish the race or practice session before assigning or returning rental transponders.");
        _settings.Events = Events.ToList(); _settings.Drivers = Drivers.ToList(); _settings.Classes = Classes.ToList();
        var before = JsonSerializer.Serialize(_settings.RentalTransponders);
        var roster = Events.Select(e => (Event: e, Assignments: e.DriverAssignments.ToList())).ToList();
        var assignments = Events.SelectMany(e => e.DriverAssignments).Select(a => (Row: a, Number: a.TransponderId)).ToList();
        var entrants = Events.SelectMany(e => e.Sessions).SelectMany(s => s.Entrants).Select(e => (Row: e, Number: e.TransponderId)).ToList();
        IsEnabled = false;
        try
        {
            mutation();
            await CaptureAndSaveSettingsAsync(reportFailureToCaller: true);
        }
        catch
        {
            _settings.RentalTransponders = JsonSerializer.Deserialize<RentalTransponderState>(before)!;
            foreach (var r in roster) r.Event.DriverAssignments = r.Assignments;
            foreach (var a in assignments) a.Row.TransponderId = a.Number;
            foreach (var e in entrants) e.Row.TransponderId = e.Number;
            throw;
        }
        finally
        {
            IsEnabled = true;
            if (changesAssignments)
            {
                if (_activeEvent is not null) RefreshEventDriverChoices();
                if (_loadedSession is not null && _loadedSession.ProgressStatus == EventSessionStatus.Loaded)
                    foreach (var racer in SessionRacers)
                    {
                        var entrant = _loadedSession.Entrants.FirstOrDefault(x => x.DriverId == racer.DriverId);
                        if (entrant is not null) racer.TransponderId = entrant.TransponderId;
                    }
            }
        }
    }
}
