using IMRCRaceControl.Models;
namespace IMRCRaceControl.Core;
public static class EventRegistration
{
    public static bool Set(RaceDayEventDefinition ev, string driverId, string classId, bool included, string transponder, int seed = 0)
    {
        if (ev.Status != EventStatus.Active) throw new InvalidOperationException("Reopen this event before changing registrations.");
        if (string.IsNullOrWhiteSpace(driverId) || string.IsNullOrWhiteSpace(classId)) throw new InvalidOperationException("Select a driver and class.");
        var existing=ev.DriverAssignments.FirstOrDefault(a=>a.DriverId==driverId && a.ClassId==classId);
        var changed=included != (existing is not null);
        if(included)
        {
            if(!ev.ClassIds.Contains(classId))ev.ClassIds.Add(classId);
            if(existing is null) { existing=new EventDriverAssignment { DriverId=driverId,ClassId=classId,SeedRank=seed };ev.DriverAssignments.Add(existing); }
            existing.TransponderId=transponder.Trim();
        }
        else if(existing is not null)ev.DriverAssignments.Remove(existing);
        // Registration never removes or rebuilds saved races or changes results.
        return changed && ev.Sessions.Any(s=>s.ClassId==classId);
    }
}
