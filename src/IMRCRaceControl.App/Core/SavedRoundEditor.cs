using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

public static class SavedRoundEditor
{
    public static bool PreservesCompletedResults(IEnumerable<EventSessionDefinition> existing,
        IReadOnlyList<EventSessionDefinition> replacement) => existing
        .Where(s => s.ProgressStatus == EventSessionStatus.Completed)
        .All(s => replacement.Any(r => ReferenceEquals(s, r)));

    public static void ReplaceScope(RaceDayEventDefinition raceEvent,
        IReadOnlyList<EventSessionDefinition> existing, IReadOnlyList<EventSessionDefinition> replacement)
    {
        if (!PreservesCompletedResults(existing, replacement))
            throw new InvalidOperationException("Completed results cannot be replaced by lineup edits.");
        // Rebuilding a saved scope may create new IDs; retain its class/heat numbers.
        foreach (var session in replacement.Where(s => s.RaceNumber <= 0))
            session.RaceNumber = existing.FirstOrDefault(old => old.SessionType == session.SessionType &&
                string.Equals(old.ClassId, session.ClassId, StringComparison.OrdinalIgnoreCase) &&
                old.Heat == session.Heat && old.RaceNumber > 0)?.RaceNumber ?? 0;
        foreach (var session in existing) raceEvent.Sessions.Remove(session);
        foreach (var session in replacement)
        {
            raceEvent.Sessions.Add(session);
            // Late additions also join the event roster for subsequent rounds.
            foreach (var entrant in session.Entrants)
                if (!raceEvent.DriverAssignments.Any(a => a.DriverId == entrant.DriverId && a.ClassId == session.ClassId))
                    raceEvent.DriverAssignments.Add(new EventDriverAssignment {
                        DriverId = entrant.DriverId, ClassId = session.ClassId,
                        TransponderId = entrant.TransponderId, SeedRank = entrant.SeedRank });
        }
    }
}
