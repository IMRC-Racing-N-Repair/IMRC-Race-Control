using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

public static class OverallPositionScope
{
    public static List<EventSessionDefinition> Sessions(IEnumerable<EventSessionDefinition> sessions, EventSessionDefinition loaded)
    {
        if (loaded.SessionType == SessionType.Race)
            return loaded.CombinedMembers.Where(s=>s.ProgressStatus!=EventSessionStatus.Skipped).ToList();
        var classes = (loaded.IsCombinedRace ? loaded.CombinedMembers.Select(s => s.ClassId) : [loaded.ClassId])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return sessions.Where(s => s.SessionType == SessionType.Qualifying && s.Round == loaded.Round &&
            s.ProgressStatus != EventSessionStatus.Skipped && classes.Contains(s.ClassId))
            .OrderBy(s => s.Heat).ThenBy(s => s.QueueOrder).ToList();
    }

    public static bool IsCurrent(EventSessionDefinition loaded, EventSessionDefinition session) =>
        string.Equals(session.Id, loaded.Id, StringComparison.OrdinalIgnoreCase) ||
        loaded.CombinedMembers.Any(m => string.Equals(m.Id, session.Id, StringComparison.OrdinalIgnoreCase));

    public static Dictionary<(string, int), int> LiveCars(EventSessionDefinition loaded) => loaded.Entrants
        .ToDictionary(e => (loaded.IsCombinedRace ? e.SourceSessionId : loaded.Id,
            loaded.IsCombinedRace ? e.SourceCarNumber : e.CarNumber), e => e.CarNumber);

    public static Dictionary<string,int> Positions<T>(IEnumerable<T> ranked, Func<T,string> classId, Func<T,string> key) =>
        ranked.GroupBy(classId,StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group.Select((row,index) => (Key:key(row),Position:index+1)))
            .ToDictionary(x => x.Key,x => x.Position,StringComparer.OrdinalIgnoreCase);
}
