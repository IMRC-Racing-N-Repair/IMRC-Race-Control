using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

/// <summary>
/// Detects racers scheduled in adjacent ON-TRACK races, not adjacent logical
/// class sessions. Combined classes count as a single race.
/// The caller supplies the intended on-track race order.
/// </summary>
public static class ConsecutiveRaceDetector
{
    public sealed record Conflict(string DriverId, string DriverName, string FirstRace, string SecondRace);

    public static List<Conflict> Find(IEnumerable<EventSessionDefinition> scheduledSessions)
    {
        var actualRaces = scheduledSessions
            .Where(s => s.ProgressStatus != EventSessionStatus.Skipped)
            .GroupBy(s => string.IsNullOrWhiteSpace(s.CombinedRaceId)
                ? "session:" + s.Id
                : "combined:" + s.CombinedRaceId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.ToList())
            .ToList();

        var conflicts = new List<Conflict>();
        for (var i = 0; i + 1 < actualRaces.Count; i++)
        {
            var first = actualRaces[i];
            var second = actualRaces[i + 1];
            // No warning for immutable historical pairs.
            if (first.All(s => s.ProgressStatus == EventSessionStatus.Completed) &&
                second.All(s => s.ProgressStatus == EventSessionStatus.Completed))
                continue;
            var firstDrivers = first.SelectMany(s => s.Entrants)
                .Where(e => !string.IsNullOrWhiteSpace(e.DriverId))
                .GroupBy(e => e.DriverId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().DisplayName,
                    StringComparer.OrdinalIgnoreCase);
            var firstLabel = DescribeRace(first);
            var secondLabel = DescribeRace(second);
            foreach (var entrant in second.SelectMany(s => s.Entrants)
                .Where(e => firstDrivers.ContainsKey(e.DriverId))
                .GroupBy(e => e.DriverId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()))
            {
                conflicts.Add(new Conflict(entrant.DriverId, firstDrivers[entrant.DriverId],
                    firstLabel, secondLabel));
            }
        }
        return conflicts;
    }

    private static string DescribeRace(List<EventSessionDefinition> sessions)
    {
        var first = sessions[0];
        var number = first.RaceNumber > 0 ? $"Race {first.RaceNumber}: " : "";
        var names = string.Join(" + ", sessions.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase));
        return number + names;
    }
}
