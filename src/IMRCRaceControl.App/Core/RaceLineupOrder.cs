using IMRCRaceControl.Models;
namespace IMRCRaceControl.Core;
public static class RaceLineupOrder
{
    public static string GroupKey(EventSessionDefinition session) =>
        session.SessionType == SessionType.Race
            ? "MAINS"
            : DashboardRoundChoice.From(session).ToString() ?? "";

    public static bool CanMove(EventSessionDefinition source,EventSessionDefinition target) =>
        source.Id!=target.Id && GroupKey(source)==GroupKey(target) &&
        Movable(source) && Movable(target);
    public static bool Movable(EventSessionDefinition s)=>s.ProgressStatus is EventSessionStatus.Planned or EventSessionStatus.Loaded;
    public static void RebuildRemainingQueue(IReadOnlyList<EventSessionDefinition> sessions,IReadOnlyList<string> classIds)
    {
        var completed = sessions
            .Where(s => s.ProgressStatus is EventSessionStatus.Completed or EventSessionStatus.Skipped)
            .OrderBy(s => s.QueueOrder)
            .ToList();
        var remaining = sessions
            .Where(s => s.ProgressStatus is not EventSessionStatus.Completed and not EventSessionStatus.Skipped)
            .ToList();

        var classOrder = classIds
            .Select((id, index) => new { id, index })
            .ToDictionary(x => x.id, x => x.index, StringComparer.OrdinalIgnoreCase);

        remaining = remaining
            .OrderBy(s => s.SessionType == SessionType.Qualifying ? 0 : 1)
            .ThenBy(s => s.SessionType == SessionType.Race ? -s.Heat : s.Round)
            .ThenBy(s => classOrder.TryGetValue(s.ClassId, out var index) ? index : int.MaxValue)
            .ThenBy(s => s.SessionType == SessionType.Race ? s.MainLeg : s.Heat)
            .ToList();

        // Completed/skipped race queue positions are immutable. Reuse only
        // available unrun queue slots when rebuilding the event order.
        var fixedSlots = completed.Select(s => s.QueueOrder).Where(q => q > 0).ToHashSet();
        var availableSlots = sessions.Select(s => s.QueueOrder)
            .Where(q => q > 0 && !fixedSlots.Contains(q)).Distinct().OrderBy(q => q).ToList();
        var nextSlot = Math.Max(0, sessions.Select(s => s.QueueOrder).DefaultIfEmpty(0).Max()) + 1;
        for (var i = 0; i < remaining.Count; i++)
            remaining[i].QueueOrder = i < availableSlots.Count ? availableSlots[i] : nextSlot++;
        Apply(sessions);
    }

    public static void Apply(IEnumerable<EventSessionDefinition> sessions)
    {
        var all=sessions.ToList();
        foreach(var group in all.GroupBy(GroupKey))
        {
            var movable=group.Where(Movable).OrderBy(s=>s.QueueOrder).ToList();
            if(!movable.Any(s=>s.LineupOrder.HasValue))continue;
            var slots=movable.Select(s=>s.QueueOrder).ToArray();
            var ordered=movable.OrderBy(s=>s.LineupOrder??int.MaxValue).ThenBy(s=>s.QueueOrder).ToList();
            for(int i=0;i<ordered.Count;i++)ordered[i].QueueOrder=slots[i];
        }
        RaceSchedule.RenumberManualRounds(all);
    }
}
