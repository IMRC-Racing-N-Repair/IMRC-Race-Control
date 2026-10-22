using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

public sealed record DashboardRoundChoice(SessionType Type, int Number)
{
    public string Label => $"{(Type == SessionType.Qualifying ? "Q" : Type == SessionType.Race ? "M" : "P")}{Number}";
    public static DashboardRoundChoice From(EventSessionDefinition session) =>
        new(session.SessionType, session.SessionType == SessionType.Race ? session.MainLeg : session.Round);
    public bool Matches(EventSessionDefinition session) => From(session) == this;
}

public static class RaceSchedule
{
    public static IOrderedEnumerable<EventSessionDefinition> DisplayOrder(IEnumerable<EventSessionDefinition> sessions) =>
        sessions.OrderBy(s => s.ScheduleStageOrder).ThenBy(s => s.ScheduleRoundOrder)
            .ThenBy(s => s.RaceNumber).ThenBy(s => s.QueueOrder);

    public static List<DashboardRoundChoice> AvailableRounds(IEnumerable<EventSessionDefinition> sessions) =>
        sessions.Where(s => s.ProgressStatus != EventSessionStatus.Skipped)
            .Select(DashboardRoundChoice.From).Distinct()
            .OrderBy(r => r.Type == SessionType.Qualifying ? 0 : r.Type == SessionType.Race ? 1 : 2)
            .ThenBy(r => r.Number).ToList();

    public static List<EventSessionDefinition> InRound(IEnumerable<EventSessionDefinition> sessions, DashboardRoundChoice? round) =>
        sessions.Where(s => round is not null && round.Matches(s) && s.ProgressStatus != EventSessionStatus.Skipped)
            .OrderBy(s => s.QueueOrder > 0 ? s.QueueOrder : int.MaxValue).ToList();

    public static EventSessionDefinition? InitialRace(IEnumerable<EventSessionDefinition> sessions) =>
        sessions.FirstOrDefault(s => s.ProgressStatus is EventSessionStatus.Loaded or EventSessionStatus.Running)
        ?? sessions.FirstOrDefault(s => s.ProgressStatus == EventSessionStatus.Planned)
        ?? sessions.FirstOrDefault(s => s.ProgressStatus != EventSessionStatus.Skipped);

    public static void AssignRaceNumbers(IEnumerable<EventSessionDefinition> sessions)
    {
        // A class/heat slot has one number across qualifying rounds. Each class/main
        // letter similarly keeps its number across M1/M2/etc. Stages number separately.
        // Reserve saved numbers before allocating new slots, so additions and queue
        // reordering cannot shift existing numbers. Gaps are intentional after removals.
        foreach (var stage in sessions.GroupBy(s => s.SessionType))
        {
            var slots = stage.OrderBy(s => DashboardRoundChoice.From(s).Number)
                .ThenBy(s => s.QueueOrder > 0 ? s.QueueOrder : int.MaxValue)
                .ThenBy(s => s.ClassName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.SessionType == SessionType.Race ? -s.Heat : s.Heat)
                .GroupBy(s => (s.ClassId.ToUpperInvariant(), s.Heat)).ToList();
            var numbers = new Dictionary<(string, int), int>();
            var used = new HashSet<int>();
            foreach (var slot in slots)
            {
                var saved = slot.Select(s => s.RaceNumber).FirstOrDefault(n => n > 0 && !used.Contains(n));
                if (saved <= 0) continue;
                numbers[slot.Key] = saved;
                used.Add(saved);
            }
            var next = used.DefaultIfEmpty(0).Max() + 1;
            foreach (var slot in slots)
            {
                if (!numbers.TryGetValue(slot.Key, out var number)) number = next++;
                foreach (var session in slot) session.RaceNumber = number;
            }
        }
    }
}
