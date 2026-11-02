using IMRCRaceControl.Models;
namespace IMRCRaceControl.Core;
public static class HeatDriverTransfer
{
    public static void Validate(IReadOnlyList<SessionEntrant> source,IReadOnlyList<SessionEntrant> target,int capacity)
    {
        if(source.Count+target.Count>capacity)throw new InvalidOperationException($"The destination can hold {capacity} drivers. Choose another race or move drivers individually first.");
        var all=target.Concat(source).ToList();
        if(all.Where(e=>!string.IsNullOrWhiteSpace(e.DriverId)).GroupBy(e=>e.DriverId,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))throw new InvalidOperationException("A driver is already in the destination race.");
        if(all.Where(e=>!string.IsNullOrWhiteSpace(e.TransponderId)).GroupBy(e=>e.TransponderId,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))throw new InvalidOperationException("A transponder is already in the destination race.");
    }
}
