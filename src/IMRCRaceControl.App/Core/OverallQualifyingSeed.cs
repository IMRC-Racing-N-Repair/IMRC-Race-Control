using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

/// <summary>
/// One best completed qualifying run per driver across every round. Does not
/// mutate the stored race results or depend on the last round's placements.
/// </summary>
public static class OverallQualifyingSeed
{
    public static List<SessionEntrant> SortEntrants(IEnumerable<EventSessionDefinition> sessions,
        string classId, IEnumerable<SessionEntrant> entrants, int? beforeRound = null)
    {
        var order = BestRunOrder(sessions, classId, beforeRound)
            .Select((id, index) => (id, index))
            .ToDictionary(x => x.id, x => x.index, StringComparer.OrdinalIgnoreCase);
        return entrants.OrderBy(e => order.TryGetValue(e.DriverId, out var rank) ? rank : int.MaxValue)
            .ThenBy(e => e.SeedRank <= 0 ? int.MaxValue : e.SeedRank)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> BestRunOrder(IEnumerable<EventSessionDefinition> sessions, string classId, int? beforeRound = null)
    {
        return sessions
            .Where(s => s.SessionType == SessionType.Qualifying &&
                        string.Equals(s.ClassId, classId, StringComparison.OrdinalIgnoreCase) &&
                        s.ProgressStatus == EventSessionStatus.Completed &&
                        (!beforeRound.HasValue || s.Round < beforeRound.Value))
            .SelectMany(session => session.Entrants
                .Where(e => !string.IsNullOrWhiteSpace(e.DriverId))
                .Select(entrant =>
                {
                    var result = session.Results.FirstOrDefault(r => r.CarNumber == entrant.CarNumber);
                    var laps = session.Laps.Where(l => l.CarNumber == entrant.CarNumber)
                        .OrderBy(l => l.LapNumber).ToList();
                    return new
                    {
                        entrant.DriverId,
                        Name = entrant.DisplayName,
                        Laps = result?.Laps ?? laps.Count,
                        Elapsed = laps.LastOrDefault()?.Elapsed.TotalSeconds ?? double.MaxValue,
                        BestLap = laps.Count == 0 ? double.MaxValue : laps.Min(l => l.LapTime.TotalSeconds)
                    };
                }))
            .Where(p => p.Laps > 0)
            .GroupBy(p => p.DriverId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(p => p.Laps)
                .ThenBy(p => p.Elapsed).ThenBy(p => p.BestLap).First())
            .OrderByDescending(p => p.Laps).ThenBy(p => p.Elapsed)
            .ThenBy(p => p.BestLap).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.DriverId).ToList();
    }
}
