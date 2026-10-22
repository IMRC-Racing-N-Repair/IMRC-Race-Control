using IMRCRaceControl.Models;
namespace IMRCRaceControl.Core;
public sealed class RaceReport
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string EmptyMessage { get; set; } = "No results or lineups match this selection.";
    public bool IsLineup { get; set; }
    public List<ReportSection> Sections { get; } = [];
}
public sealed record ReportSection(string Title, string[] Columns, List<string[]> Rows, string Notes = "", string RoundHeading = "");
public sealed record EventReportScope(SessionType? Type = null, int? Round = null)
{
    public string Label => Type == SessionType.Qualifying ? $"Qualifying round {Round}" : Type == SessionType.Race ? "Mains" : "All rounds / mains";
    public bool Matches(EventSessionDefinition session) =>
        (Type is null || session.SessionType == Type) && (Round is null || session.Round == Round);
    public static EventReportScope InitialSelection(IReadOnlyList<EventReportScope> choices, EventReportScope? previous = null) =>
        choices.FirstOrDefault(choice => choice == previous)
        ?? choices.FirstOrDefault(choice => choice.Type is not null)
        ?? new EventReportScope();

    public static List<EventReportScope> Available(RaceDayEventDefinition? raceEvent)
    {
        var choices = new List<EventReportScope> { new() };
        if (raceEvent is null) return choices;
        choices.AddRange(raceEvent.Sessions.Where(s => s.SessionType == SessionType.Qualifying)
            .Select(s => s.Round).Distinct().Order().Select(round => new EventReportScope(SessionType.Qualifying, round)));
        if (raceEvent.Sessions.Any(s => s.SessionType == SessionType.Race)) choices.Add(new(SessionType.Race));
        return choices;
    }
}
public static class RaceReports
{
    private static string Number(decimal n) => n.ToString("0.##");
    public static RaceReport Lineups(RaceDayEventDefinition ev, string? classId = null, EventReportScope? scope = null)
    {
        scope ??= new EventReportScope();
        RaceSchedule.AssignRaceNumbers(ev.Sessions);
        var report = new RaceReport { Title = "Race event lineups", IsLineup = true,
            Subtitle = $"{ev.Name} • {ev.EventDate:MM/dd/yyyy} • {scope.Label}",
            EmptyMessage = "No saved lineups match this event, round and class. Save the round in Rounds / Mains first; races do not need to be completed." };
        foreach (var s in RaceSchedule.DisplayOrder(ev.Sessions.Where(s=>(classId is null || s.ClassId==classId) && scope.Matches(s))))
        {
            var roundHeading = s.SessionType == SessionType.Qualifying ? $"Qualifying Round {s.Round}"
                : s.SessionType == SessionType.Race ? $"Main Round {s.MainLeg}" : $"Practice Round {s.Round}";
            var raceHeading = $"RACE #{s.RaceNumber} - {s.ClassName} - " +
                (s.SessionType == SessionType.Race ? $"{s.HeatMainDisplay}-MAIN" : $"HEAT {s.Heat}");
            report.Sections.Add(new ReportSection(raceHeading, ["Car","Driver","Transponder","Seed"],
                s.Entrants.OrderBy(e=>e.CarNumber).Select(e=>new[] { e.CarNumber.ToString(),e.DisplayName,e.TransponderId,e.SeedRank.ToString() }).ToList(),
                RoundHeading: roundHeading));
        }
        return report;
    }
    public static RaceReport EventStandings(RaceDayEventDefinition ev, string? classId = null, EventReportScope? scope = null)
    {
        scope ??= new EventReportScope();
        var report = new RaceReport { Title="Race event completed results", Subtitle=$"{ev.Name} • {ev.EventDate:MM/dd/yyyy} • {scope.Label}",
            EmptyMessage="No completed results match this selection yet. To print racer placements before racing, choose Event / round lineups in the report menu." };
        foreach (var group in ev.Sessions.Where(s=>(classId is null || s.ClassId==classId) && scope.Matches(s)).GroupBy(s=>s.ClassId))
        {
            var cls=group.First().ClassName;
            var q=scope.Type == SessionType.Race ? [] : SeriesScoring.QualifyingOrder(ev,group.Key,scope.Round);
            if (q.Count>0) report.Sections.Add(new ReportSection($"{cls} • {(scope.Round.HasValue ? scope.Label : "Overall qualifying")}",["Position","Driver"],q.Select((d,i)=>new[] {(i+1).ToString(),d.DriverName}).ToList()));
            var m=scope.Type == SessionType.Qualifying ? [] : SeriesScoring.MainOrder(ev,group.Key);
            if (m.Count>0) report.Sections.Add(new ReportSection($"{cls} • Overall mains",["Position","Driver"],m.Select((d,i)=>new[] {(i+1).ToString(),d.DriverName}).ToList(),"Repeated mains use the configured best runs. Incomplete main groups are excluded."));
            foreach (var s in group.Where(s=>s.ProgressStatus==EventSessionStatus.Completed).OrderBy(s=>s.QueueOrder))
                report.Sections.Add(new ReportSection(s.Name,["Pos","Car","Driver","Laps","Total time","Fast lap"],s.Results.OrderBy(r=>r.Position).Select(r=>new[] {r.Position.ToString(),r.CarNumber.ToString(),r.DriverName,r.Laps.ToString(),r.TotalTime,r.BestLap}).ToList()));
        }
        return report;
    }
    public static RaceReport Series(RaceSeriesDefinition series, IEnumerable<RaceDayEventDefinition> events, string? classId=null, IEnumerable<DriverProfile>? drivers=null, IEnumerable<ClassDefinition>? classDefinitions=null)
    {
        var allEvents=events.ToList();
        var selected=SeriesScoring.SelectedEvents(series,allEvents);
        var report=new RaceReport { Title=$"Series standings — {series.Name}", Subtitle=$"{series.StartDate:MM/dd/yyyy}–{series.EndDate:MM/dd/yyyy} • "+(series.BestEventCount>0 ? $"Best {series.BestEventCount}" : "All events count")+ $" • {selected.Count} selected events" };
        var classNames=(classDefinitions ?? []).ToDictionary(c=>c.Id,c=>c.Name);
        var classes=selected.SelectMany(e=>e.Sessions.Select(s=>(s.ClassId,s.ClassName)))
            .Concat(selected.SelectMany(e=>e.DriverAssignments).Select(a=>(a.ClassId,ClassName:classNames.GetValueOrDefault(a.ClassId,"Class")))).Where(c=>classId is null || c.ClassId==classId).GroupBy(c=>c.ClassId).Select(g=>g.First());
        foreach (var cls in classes)
        {
            var rows=SeriesScoring.Calculate(series,allEvents,cls.ClassId,drivers);
            if (rows.Count == 0) continue;
            report.Sections.Add(new ReportSection(cls.ClassName,["Pos","Driver","Points","Attended","Counted","Dropped pts"],rows.Select(r=>new[] {r.Position.ToString(),r.DriverName,Number(r.Points),r.Events.ToString(),r.CountedEvents.ToString(),Number(r.DroppedPoints)}).ToList()));
            foreach (var row in rows)
            {
                report.Sections.Add(new ReportSection($"{cls.ClassName} • {row.DriverName}",["Event","Qual","Main","Bonus","Adjust","Total","Status"], row.Weeks.Select(w=>new[] {$"{w.EventDate:MM/dd} {w.EventName}",Number(w.Qualifying),Number(w.Mains),Number(w.Bonuses),Number(w.Adjustment),Number(w.Total),w.Status}).ToList(),$"Series-total adjustments (always counted): {row.ManualPoints:0.##}"));
                var adjustments=series.Adjustments.Where(a=>a.ClassId==cls.ClassId && a.DriverId==row.DriverId).ToList();
                if (adjustments.Count>0) report.Sections.Add(new ReportSection($"{row.DriverName} • Adjustment notes",["Event / total","Points","Reason","Entered"],adjustments.Select(a=>new[] {a.EventName,Number(a.Amount),a.Note,a.CreatedAt.LocalDateTime.ToString("g")}).ToList()));
            }
        }
        return report;
    }
    public static RaceReport Practice(string driverName, DateTime start, DateTime end, IEnumerable<(PracticeHistoryRow Stint,IReadOnlyList<PracticeHistoryLapRow> Laps)> stints)
    {
        var report=new RaceReport { Title=$"Practice — {driverName}",Subtitle=$"{start:MM/dd/yyyy}–{end:MM/dd/yyyy}" };
        foreach (var (stint,laps) in stints.OrderBy(s=>s.Stint.StartedAt))
            report.Sections.Add(new ReportSection($"{stint.DateDisplay} • {stint.TimeDisplay}",["Lap","Lap time (s)","Crossing time"],laps.Select(l=>new[] {l.LapNumber.ToString(),l.LapTimeDisplay,l.TimeDisplay}).ToList(),$"{stint.LapCount} laps • Best {stint.BestLapDisplay} • Average {stint.AverageLapDisplay} • Transponder {stint.TransponderId}"));
        return report;
    }
}
