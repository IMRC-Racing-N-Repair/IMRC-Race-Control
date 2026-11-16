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

        // Schedule one consolidated asynchronous save after the user stops
        // clicking. Closing/critical race operations flush the final state.
        _ = CaptureAndSaveSettingsAsync();
        if(warn)
            FooterStatusText.Text="Registration updated. Existing lineups are preserved; update and reprint affected heats in Open Rounds / Mains.";
        return Task.CompletedTask;
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
