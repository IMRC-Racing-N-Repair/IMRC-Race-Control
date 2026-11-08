using System.Text.Json;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

/// <summary>One scheduled run, independent original class sessions and car-number histories.</summary>
public static class CombinedRaces
{
    public static List<EventSessionDefinition> Members(IEnumerable<EventSessionDefinition> sessions, EventSessionDefinition selected) =>
        selected.IsCombinedRace ? selected.CombinedMembers : string.IsNullOrEmpty(selected.CombinedRaceId) ? [selected] :
        sessions.Where(s => s.CombinedRaceId == selected.CombinedRaceId)
            .OrderBy(s => s.CombinedRaceOrder).ThenBy(s => s.QueueOrder).ToList();

    public static void Validate(IReadOnlyList<EventSessionDefinition> members, IReadOnlyList<ClassDefinition> classes)
    {
        if (members.Count < 2) throw new InvalidOperationException("Select at least two races.");
        var first = members[0];
        if (members.Any(s => s.ProgressStatus is not (EventSessionStatus.Planned or EventSessionStatus.Loaded)))
            throw new InvalidOperationException("Only unrun races can be combined or separated. Restart completed races first.");
        if (members.Any(s => DashboardRoundChoice.From(s) != DashboardRoundChoice.From(first)))
            throw new InvalidOperationException("Choose races in the same qualifying or mains round.");
        double Minutes(EventSessionDefinition s) => s.RaceMinutesOverride ?? classes.FirstOrDefault(c => c.Id == s.ClassId)?.MinutesFor(s.SessionType) ?? 5;
        if (members.Any(s => s.RaceFormat != first.RaceFormat || s.StartStyle != first.StartStyle ||
            (s.RaceFormat == RaceFormat.Timed ? Math.Abs(Minutes(s)-Minutes(first)) > .00001 : s.TargetLaps != first.TargetLaps)))
            throw new InvalidOperationException("Combined races must use the same clock style, race format, and duration or lap target.");
        var entrants = members.SelectMany(s => s.Entrants).ToList();
        if (entrants.Count > 30) throw new InvalidOperationException("A combined race can have at most 30 drivers on track.");
        if (entrants.SelectMany(e => e.TransponderId.Split(new[]{';',',',' '},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)).GroupBy(t => t, StringComparer.OrdinalIgnoreCase).Any(g => g.Count()>1))
            throw new InvalidOperationException("A transponder is used more than once in these races. Assign unique transponders first.");
        if (entrants.Where(e => !string.IsNullOrEmpty(e.DriverId)).GroupBy(e => e.DriverId, StringComparer.OrdinalIgnoreCase).Any(g => g.Count()>1))
            throw new InvalidOperationException("A driver cannot race in two classes in the same on-track run.");
        // Do not combine a bump-up source and its destination into one simultaneous run.
        if (members.Any(s => members.Any(t => t.Id == s.BumpTargetSessionId)))
            throw new InvalidOperationException("A bump-up race and its destination must run separately.");
    }

    public static EventSessionDefinition Run(IEnumerable<EventSessionDefinition> all, EventSessionDefinition selected)
    {
        if (selected.IsCombinedRace) return selected;
        var members = Members(all, selected);
        if (members.Count < 2) return selected;
        var lead = members[0];
        var run = Clone(lead);
        run.CombinedMembers = members;
        run.Name = $"Race #{lead.RaceNumber} — Combined Classes: {string.Join(" + ", members.Select(s => s.ClassName).Distinct())}";
        run.ClassName = string.Join(" + ", members.Select(s => s.ClassName).Distinct());
        run.Entrants = []; run.Results = []; run.Laps = []; run.Breakouts = [];
        foreach (var member in members)
            foreach (var original in member.Entrants.OrderBy(e => e.CarNumber))
            {
                var entrant = Clone(original);
                entrant.SourceSessionId = member.Id;
                entrant.SourceCarNumber = original.CarNumber;
                entrant.CarNumber = run.Entrants.Count + 1;
                run.Entrants.Add(entrant);
                foreach (var result in member.Results.Where(r => r.CarNumber == original.CarNumber))
                {
                    var row = Clone(result); row.CarNumber = entrant.CarNumber;
                    row.ScoringGroupId = member.Id; row.ClassName = member.ClassName;
                    run.Results.Add(row);
                }
                run.Laps.AddRange(member.Laps.Where(l => l.CarNumber == original.CarNumber).Select(l => l with {CarNumber=entrant.CarNumber}));
                run.Breakouts.AddRange(member.Breakouts.Where(l => l.CarNumber == original.CarNumber).Select(l => l with {CarNumber=entrant.CarNumber}));
            }
        run.Laps = run.Laps.OrderBy(l => l.Timestamp).Select((l,i) => l with {Sequence=i+1L}).ToList();
        run.Breakouts = run.Breakouts.OrderBy(l => l.Timestamp).Select((l,i) => l with {Sequence=i+1L}).ToList();
        return run;
    }

    public static List<EventSessionDefinition> Runs(IEnumerable<EventSessionDefinition> sessions)
    {
        var all = sessions.ToList(); var seen = new HashSet<string>(); var result = new List<EventSessionDefinition>();
        foreach (var s in all.OrderBy(s => s.QueueOrder))
        {
            if (!string.IsNullOrEmpty(s.CombinedRaceId) && !seen.Add(s.CombinedRaceId)) continue;
            result.Add(Run(all,s));
        }
        return result;
    }

    public static void WriteBack(EventSessionDefinition run)
    {
        if (!run.IsCombinedRace) return;
        // Ending an event marks the original logical sessions skipped before saving.
        // Preserve that event-level transition instead of reviving a detached loaded run.
        if(run.CombinedMembers.All(m=>m.ProgressStatus==EventSessionStatus.Skipped))run.ProgressStatus=EventSessionStatus.Skipped;
        if(run.ProgressStatus==EventSessionStatus.Skipped && run.CombinedMembers.All(m=>m.ProgressStatus==EventSessionStatus.Planned))run.ProgressStatus=EventSessionStatus.Planned;
        foreach (var member in run.CombinedMembers)
        {
            member.ProgressStatus=run.ProgressStatus; member.StartedAt=run.StartedAt; member.CompletedAt=run.CompletedAt;
            member.RaceMinutesOverride=run.RaceMinutesOverride; member.RaceFormat=run.RaceFormat; member.TargetLaps=run.TargetLaps; member.StartStyle=run.StartStyle;
            var map=run.Entrants.Where(e=>e.SourceSessionId==member.Id).ToDictionary(e=>e.CarNumber,e=>e.SourceCarNumber);
            foreach(var entrant in run.Entrants.Where(e=>e.SourceSessionId==member.Id))
            {
                var original=member.Entrants.FirstOrDefault(e=>e.CarNumber==entrant.SourceCarNumber);
                if(original is not null)original.TransponderId=entrant.TransponderId;
            }
            member.Results=run.Results.Where(r=>map.ContainsKey(r.CarNumber)).Select(r=>{var copy=Clone(r);copy.CarNumber=map[r.CarNumber];return copy;}).ToList();
            member.Laps=run.Laps.Where(l=>map.ContainsKey(l.CarNumber)).Select(l=>l with {CarNumber=map[l.CarNumber]}).ToList();
            member.Breakouts=run.Breakouts.Where(l=>map.ContainsKey(l.CarNumber)).Select(l=>l with {CarNumber=map[l.CarNumber]}).ToList();
        }
    }

    public static int TrackCar(IEnumerable<EventSessionDefinition> all,EventSessionDefinition session,int car)
    {
        if(string.IsNullOrEmpty(session.CombinedRaceId) || session.IsCombinedRace)return car;
        var run=Run(all,session);
        return run.Entrants.FirstOrDefault(e=>e.SourceSessionId==session.Id && e.SourceCarNumber==car)?.CarNumber ?? car;
    }

    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
