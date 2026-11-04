using IMRCRaceControl.Models;
namespace IMRCRaceControl.Core;
public static class RaceLineupOrder
{
    public static bool CanMove(EventSessionDefinition source,EventSessionDefinition target) =>
        source.Id!=target.Id && DashboardRoundChoice.From(source)==DashboardRoundChoice.From(target) &&
        Movable(source) && Movable(target);
    public static bool Movable(EventSessionDefinition s)=>s.ProgressStatus is EventSessionStatus.Planned or EventSessionStatus.Loaded;
    public static void Apply(IEnumerable<EventSessionDefinition> sessions)
    {
        foreach(var group in sessions.GroupBy(DashboardRoundChoice.From))
        {
            var movable=group.Where(Movable).OrderBy(s=>s.QueueOrder).ToList();
            if(!movable.Any(s=>s.LineupOrder.HasValue))continue;
            var slots=movable.Select(s=>s.QueueOrder).ToArray();
            var ordered=movable.OrderBy(s=>s.LineupOrder??int.MaxValue).ThenBy(s=>s.QueueOrder).ToList();
            for(int i=0;i<ordered.Count;i++)ordered[i].QueueOrder=slots[i];
        }
    }
}
