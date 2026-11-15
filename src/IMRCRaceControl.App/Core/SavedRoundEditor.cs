using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

public static class SavedRoundEditor
{
    public static bool CanDelete(EventSessionDefinition session) => session.ProgressStatus is not (EventSessionStatus.Completed or EventSessionStatus.Running);

    public static List<ClassDefinition> AvailablePlaceholderClasses(IEnumerable<ClassDefinition> classes,
        IEnumerable<EventSessionDefinition> sessions, DashboardRoundChoice round)
    {
        var occupied = sessions.Where(round.Matches).Select(s => s.ClassId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return classes.Where(c => !string.IsNullOrWhiteSpace(c.Id) && !occupied.Contains(c.Id))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static EventSessionDefinition CreatePlaceholder(ClassDefinition cls, DashboardRoundChoice round,
        IReadOnlyList<EventSessionDefinition> sessions)
    {
        if (round.Type is not (SessionType.Qualifying or SessionType.Race) || round.Number < 1 ||
            (round.Type == SessionType.Qualifying && round.Number > 99) ||
            (round.Type == SessionType.Race && round.Number > 10))
            throw new InvalidOperationException("Select a valid qualifying or mains round.");

        if (sessions.Any(s => round.Matches(s) && string.Equals(s.ClassId, cls.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"{cls.Name} already has a race in {round.Label}.");

        var mains = round.Type == SessionType.Race;
        var series = mains ? sessions.Where(s => s.SessionType == SessionType.Race && s.Heat == 1 &&
            string.Equals(s.ClassId, cls.Id, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.MainLeg).FirstOrDefault() : null;
        var rule = (cls.MainRules ?? []).FirstOrDefault(r => r.Order == 1);
        var runs = mains ? Math.Max(round.Number, series?.MainRunCount ?? 1) : 1;
        return new EventSessionDefinition
        {
            Name = mains ? $"{cls.Name} A Main" + (runs > 1 ? $" {round.Number}" : "") : $"{cls.Name} Q{round.Number}",
            ClassId = cls.Id,
            ClassName = cls.Name,
            SessionType = round.Type,
            Round = mains ? 1 : round.Number,
            Heat = 1,
            MainLeg = mains ? round.Number : 1,
            MainRunCount = runs,
            MainCountBest = Math.Clamp(series?.MainCountBest ?? 1, 1, runs),
            MainCapacity = mains ? Math.Clamp(series?.MainCapacity is > 0 ? series.MainCapacity : rule?.MaxRacers ?? cls.MaxRacersPerMain, 1, 30) : 0,
            StartStyle = mains ? cls.RaceStartStyle : cls.QualStartStyle,
            RaceFormat = cls.FormatFor(round.Type),
            TargetLaps = cls.LapsFor(round.Type),
            QualificationOrder = mains ? QualificationOrder.Ranked : QualificationOrder.Random,
            QueueOrder = sessions.Select(s => s.QueueOrder).DefaultIfEmpty(0).Max() + 1,
            ProgressStatus = EventSessionStatus.Planned
        };
    }

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
            if (!string.IsNullOrWhiteSpace(session.ClassId) &&
                !raceEvent.ClassIds.Contains(session.ClassId, StringComparer.OrdinalIgnoreCase))
                raceEvent.ClassIds.Add(session.ClassId);
            // Late additions also join the event roster for subsequent rounds.
            foreach (var entrant in session.Entrants)
                if (!raceEvent.DriverAssignments.Any(a => a.DriverId == entrant.DriverId && a.ClassId == session.ClassId))
                    raceEvent.DriverAssignments.Add(new EventDriverAssignment {
                        DriverId = entrant.DriverId, ClassId = session.ClassId,
                        TransponderId = entrant.TransponderId, SeedRank = entrant.SeedRank });
        }
    }
}
