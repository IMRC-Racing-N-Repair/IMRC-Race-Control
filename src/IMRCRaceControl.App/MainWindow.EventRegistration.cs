using System.Text.Json;
using System.Windows;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
namespace IMRCRaceControl;
public partial class MainWindow
{
    private void RefreshEventRegistrationView()
    {
        if(DriverRegistrationView is null || ClassFirstRegistrationPanel is null)return;
        DriverRegistrationView.Visibility=_settings.EventRegistrationDriverFirst ? Visibility.Visible : Visibility.Collapsed;
        ClassFirstRegistrationPanel.Visibility=_settings.EventRegistrationDriverFirst ? Visibility.Collapsed : Visibility.Visible;
        DriverRegistrationView.Configure(_activeEvent,Drivers,Classes,_driverAssignments,SaveEventRegistrationAsync,NewRegistrationDriver,NewRegistrationClass);
    }
    private void ShowRegistrationLineupWarning() => MessageBox.Show(this,
        "Saved rounds and races have been kept.\n\nPlace any newly added driver into a race using Open Rounds / Mains, then reprint the lineup. If removing a registration, update their saved race placements there too.",
        "Update race placement and reprint",MessageBoxButton.OK,MessageBoxImage.Warning);
    private Task SaveEventRegistrationAsync(string eventId,string driverId,string classId,bool included,string transponder)
    {
        var ev=_activeEvent ?? throw new InvalidOperationException("Select an event first.");
        if(ev.Id!=eventId)throw new InvalidOperationException("The selected event changed. Return to the original event to edit this registration.");
        if(!Drivers.Any(d=>d.Id==driverId) || !Classes.Any(c=>c.Id==classId))throw new InvalidOperationException("Select an existing driver and class.");

        // Update live in-memory state now. The event-class tile remains interactive:
        // no full SQLite save, PB recalculation, driver-grid rebuild or queue
        // reconstruction is allowed in this checkbox handler.
        var defaults=_driverAssignments.Where(a=>a.DriverId==driverId && a.ClassId==classId)
            .Select(a=>a.SeedRank).Where(n=>n>0).ToList();
        var warn=EventRegistration.Set(ev,driverId,classId,included,transponder,
            defaults.Count>0 ? defaults.Min() : 0);
        var assignment=ev.DriverAssignments.FirstOrDefault(a=>a.DriverId==driverId && a.ClassId==classId);
        if(assignment is not null) UpdateFutureSessionTransponder(assignment);

        // Copy only small membership/assignment arrays, not years of laps.
        // The ordered database worker writes them atomically. UI returns now.
        var classIdsJson = JsonSerializer.Serialize(ev.ClassIds);
        var assignmentsJson = JsonSerializer.Serialize(ev.DriverAssignments);
        var affected = new List<object>();
        if (assignment is not null)
        {
            for (var si=0;si<ev.Sessions.Count;si++)
            {
                var session=ev.Sessions[si];
                if(session.ClassId!=classId || session.ProgressStatus is not
                    (EventSessionStatus.Planned or EventSessionStatus.Loaded))continue;
                for(var ei=0;ei<session.Entrants.Count;ei++)
                {
                    var entrant=session.Entrants[ei];
                    if(entrant.DriverId!=driverId)continue;
                    affected.Add(new { SessionIndex=si, SessionId=session.Id,
                        EntrantIndex=ei, CarNumber=entrant.CarNumber,
                        Transponder=entrant.TransponderId ?? "" });
                }
            }
        }
        _ = PersistEventRegistrationChangeAsync(ev.Id, classIdsJson,
            assignmentsJson, JsonSerializer.Serialize(affected));
        if(warn)
            FooterStatusText.Text="Registration updated. Existing lineups are preserved; update and reprint affected heats in Open Rounds / Mains.";
        return Task.CompletedTask;
    }

    private async Task PersistEventRegistrationChangeAsync(string eventId, string classIds,
        string assignments, string entrantPatches)
    {
        try { await _stateWriter.UpdateEventRegistrationAsync(eventId,classIds,assignments,entrantPatches); }
        catch(Exception ex)
        {
            IMRCRaceControl.Services.AppCrashLogger.Log("Targeted driver registration save",ex);
            FooterStatusText.Text="Registration save warning. A full recovery save was queued.";
            _ = CaptureAndSaveSettingsAsync();
        }
    }

    private async void NewRegistrationDriver()
    {
        if(_activeEvent?.Status!=EventStatus.Active)return;
        var driver=ShowDriverEditor();if(driver is null)return;
        await CaptureAndSaveSettingsAsync();RefreshEventRegistrationView();DriverRegistrationView.SelectDriver(driver.Id);
    }
    private async void NewRegistrationClass()
    {
        if(_activeEvent?.Status!=EventStatus.Active)return;
        var cls=ShowClassEditor();if(cls is null)return;
        await CaptureAndSaveSettingsAsync();RefreshEventRegistrationView();
    }
}
