using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

/// <summary>
/// One best completed qualifying run per driver across every round. Does not
/// mutate the stored race results or depend on the last round's placements.
/// </summary>
public static class OverallQualifyingSeed
{
    public static List<string> BestRunOrder(IEnumerable<EventSessionDefinition> sessions, string classId)
    {
        return sessions
            .Where(s => s.SessionType == SessionType.Qualifying &&
                        string.Equals(s.ClassId, classId, StringComparison.OrdinalIgnoreCase) &&
                        s.ProgressStatus == EventSessionStatus.Completed)
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
