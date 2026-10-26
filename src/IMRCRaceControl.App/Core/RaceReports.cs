using IMRCRaceControl.Models;
namespace IMRCRaceControl.Core;
public sealed class RaceReport
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string EmptyMessage { get; set; } = "No results or lineups match this selection.";
    public bool IsLineup { get; set; }
    public bool IsLapMatrix { get; set; }
    public List<ReportSection> Sections { get; } = [];
    public Dictionary<string, ReportTextStyle> TextStyles { get; } = new(StringComparer.Ordinal);
}
public sealed record ReportSection(string Title, string[] Columns, List<string[]> Rows, string Notes = "", string RoundHeading = "", string ClassColor = "#DCE1E6", int RaceNumber = 0);
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
    public static RaceReport CompletedRace(RaceDayEventDefinition ev, EventSessionDefinition session, int? carNumber = null)
    {
        if (session.ProgressStatus != EventSessionStatus.Completed)
            throw new InvalidOperationException("Choose a completed race to export.");
        var report = new RaceReport { Title = "Race lap times", IsLapMatrix=true,
            Subtitle = $"{ev.Name} • {ev.EventDate:MM/dd/yyyy} • {session.RoundLabel} • {session.DashboardDisplay}" };
        var cars = session.Entrants.Select(e => e.CarNumber).Concat(session.Results.Select(r => r.CarNumber))
            .Concat(session.Laps.Select(l => l.CarNumber)).Distinct().Where(c => carNumber is null || c == carNumber).Order().ToList();
        string Name(int car)
        {
            var entrant=session.Entrants.FirstOrDefault(e=>e.CarNumber==car);
            if (!string.IsNullOrWhiteSpace(entrant?.Nickname)) return entrant.Nickname.Trim();
            var full=entrant?.DriverName ?? session.Results.FirstOrDefault(r=>r.CarNumber==car)?.DriverName ?? session.Laps.FirstOrDefault(l=>l.CarNumber==car)?.DriverName ?? "Driver";
            return full.Split(' ',StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "Driver";
        }
        var names=cars.ToDictionary(c=>c,Name);
        foreach(var group in cars.Chunk(8))
        {
            var columns=new[]{"Lap"}.Concat(group.Select(c=>names.Values.Count(n=>n==names[c])>1 ? $"{names[c]} #{c}" : names[c])).ToArray();
            var byCar=group.ToDictionary(c=>c,c=>session.Laps.Where(l=>l.CarNumber==c).GroupBy(l=>l.LapNumber).ToDictionary(g=>g.Key,g=>g.OrderByDescending(l=>l.Sequence).First().LapTimeDisplay));
            var lapNumbers=group.SelectMany(c=>byCar[c].Keys).Distinct().Order();
            var rows=lapNumbers.Select(n=>new[]{n.ToString()}.Concat(group.Select(c=>byCar[c].GetValueOrDefault(n,"—"))).ToArray()).ToList();
            report.Sections.Add(new ReportSection("",columns,rows,rows.Count==0 ? "No recorded laps." : ""));
        }
        return report;
    }

    public static string LapReportCsv(RaceReport report)
    {
        static string Cell(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        var lines = new List<string> { Cell(report.Subtitle) };
        foreach (var section in report.Sections)
        {
            lines.Add(string.Join(",",section.Columns.Select(Cell)));
            lines.AddRange(section.Rows.Select(row=>string.Join(",",row.Select(Cell))));
            if(section.Rows.Count==0) lines.Add(Cell(section.Notes));
            lines.Add("");
        }
        return string.Join("\r\n", lines);
    }

    public static string BestQualifyingResult(RaceDayEventDefinition ev, string classId, string driverId)
    {
        if (string.IsNullOrWhiteSpace(driverId) || driverId.StartsWith("unknown:",StringComparison.OrdinalIgnoreCase)) return "—";
        var runs=new List<(int Laps,double Seconds,string Time,int Round)>();
        foreach(var session in ev.Sessions.Where(s=>s.ClassId==classId && s.SessionType==SessionType.Qualifying && s.ProgressStatus==EventSessionStatus.Completed))
        {
            var entrants=session.Entrants.Where(e=>e.DriverId==driverId).ToList();
            if(entrants.Count!=1 || session.Entrants.Count(e=>e.CarNumber==entrants[0].CarNumber)!=1)continue;
            var results=session.Results.Where(r=>r.CarNumber==entrants[0].CarNumber).ToList();
            if(results.Count!=1 || results[0].Laps<=0)continue;
            var result=results[0];
            var parts=result.TotalTime.Split(':');
            double seconds=0;bool valid=true;
            foreach(var part in parts) { if(!double.TryParse(part,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var v)||!double.IsFinite(v)||v<0){valid=false;break;}seconds=seconds*60+v; }
            if(!valid) seconds=session.Laps.Where(l=>l.CarNumber==entrants[0].CarNumber).Select(l=>l.Elapsed.TotalSeconds).DefaultIfEmpty(double.MaxValue).Max();
            runs.Add((result.Laps,seconds,result.TotalTime,session.Round));
        }
        if(runs.Count==0)return "—";
        var best=runs.OrderByDescending(r=>r.Laps).ThenBy(r=>r.Seconds).ThenBy(r=>r.Round).First();
        return $"{best.Laps} / {best.Time} ({best.Round})";
    }

    private static string Number(decimal n) => n.ToString("0.##");
    public static RaceReport Lineups(RaceDayEventDefinition ev, string? classId = null, EventReportScope? scope = null, RentalTransponderState? rentals = null, IEnumerable<ClassDefinition>? classes = null, IEnumerable<DriverRatingRow>? ratings = null)
    {
        scope ??= new EventReportScope();
        RaceSchedule.AssignRaceNumbers(ev.Sessions);
        var report = new RaceReport { Title = "Race event lineups", IsLineup = true,
            Subtitle = $"{ev.Name} • {ev.EventDate:MM/dd/yyyy} • {scope.Label}",
            EmptyMessage = "No saved lineups match this event, round and class. Save the round in Rounds / Mains first; races do not need to be completed." };
        var rentalLabels = new RentalPrintFormatting(rentals, report);
        var ratingRows = (ratings ?? []).GroupBy(r => (r.ClassId, r.DriverId)).ToDictionary(g => g.Key, g => g.First());
        string RatingFor(EventSessionDefinition session, SessionEntrant entrant) =>
            ratingRows.TryGetValue((session.ClassId, entrant.DriverId), out var rating)
                ? $"{rating.RatingDisplay} / {rating.SafetyDisplay}" : "1500 / 3.00*";
        foreach (var s in RaceSchedule.DisplayOrder(ev.Sessions.Where(s=>(classId is null || s.ClassId==classId) && scope.Matches(s))))
        {
            var roundHeading = s.SessionType == SessionType.Qualifying ? $"Qualifying Round {s.Round}"
                : s.SessionType == SessionType.Race ? $"Main Round {s.MainLeg}" : $"Practice Round {s.Round}";
            var raceHeading = $"RACE #{s.RaceNumber} - {s.ClassName} - " +
                (s.SessionType == SessionType.Race ? $"{s.HeatMainDisplay}-MAIN" : $"HEAT {s.Heat}");
            report.Sections.Add(new ReportSection(raceHeading, ["Car","Driver","Transponder",s.SessionType == SessionType.Qualifying ? "Best qual · laps / time" : "Last round · laps / time","SOF / SR"],
                s.Entrants.OrderBy(e=>e.CarNumber).Select(e=>new[] { e.CarNumber.ToString(),e.DisplayName,rentalLabels.Format(e.TransponderId),s.SessionType == SessionType.Qualifying ? BestQualifyingResult(ev,s.ClassId,e.DriverId) : PreviousRoundResult(ev,s,e.DriverId),RatingFor(s,e) }).ToList(),
                RoundHeading: roundHeading, ClassColor: classes?.FirstOrDefault(c => c.Id == s.ClassId)?.PrintColor ?? ClassPrintColors.Choices[0].Hex, RaceNumber: s.RaceNumber));
        }
        return report;
    }
    public static string PreviousRoundResult(RaceDayEventDefinition ev, EventSessionDefinition current, string driverId)
    {
        if (string.IsNullOrWhiteSpace(driverId) || driverId.StartsWith("unknown:", StringComparison.OrdinalIgnoreCase)) return "—";
        // Join by retained driver identity, not name, car number, or transponder.
        var previous = ev.Sessions.Where(s => s.Id != current.Id && s.ClassId == current.ClassId && s.ProgressStatus == EventSessionStatus.Completed &&
            (current.SessionType == SessionType.Race
                ? s.SessionType == SessionType.Qualifying || (s.SessionType == SessionType.Race && s.MainLeg < current.MainLeg)
                : s.SessionType == current.SessionType && s.Round < current.Round))
            .OrderByDescending(s => s.SessionType == SessionType.Race ? 1 : 0)
            .ThenByDescending(s => s.SessionType == SessionType.Race ? s.MainLeg : s.Round)
            .ThenByDescending(s => s.CompletedAt).ThenByDescending(s => s.QueueOrder);
        foreach (var session in previous)
        {
            var entrants = session.Entrants.Where(e => e.DriverId == driverId).ToList();
            if (entrants.Count != 1 || session.Entrants.Count(e => e.CarNumber == entrants[0].CarNumber) != 1) continue;
            var results = session.Results.Where(r => r.CarNumber == entrants[0].CarNumber).ToList();
            if (results.Count == 1) return $"{session.RoundLabel} · {results[0].Laps} / {results[0].TotalTime}";
        }
        return "—";
    }
    public static RaceReport EventStandings(RaceDayEventDefinition ev, string? classId = null, EventReportScope? scope = null, IEnumerable<ClassDefinition>? classes = null)
    {
        scope ??= new EventReportScope();
        var report = new RaceReport { Title="Race event completed results", Subtitle=$"{ev.Name} • {ev.EventDate:MM/dd/yyyy} • {scope.Label}",
            EmptyMessage="No completed results match this selection yet. To print racer placements before racing, choose Event / round lineups in the report menu." };
        foreach (var group in ev.Sessions.Where(s=>(classId is null || s.ClassId==classId) && scope.Matches(s)).GroupBy(s=>s.ClassId))
        {
            var cls=group.First().ClassName;
            var color = classes?.FirstOrDefault(c => c.Id == group.Key)?.PrintColor ?? ClassPrintColors.Choices[0].Hex;
            var sectionStart = report.Sections.Count;
            var q=scope.Type == SessionType.Race ? [] : SeriesScoring.QualifyingOrder(ev,group.Key,scope.Round);
            if (q.Count>0) report.Sections.Add(new ReportSection($"{cls} • {(scope.Round.HasValue ? scope.Label : "Overall qualifying")}",["Position","Driver"],q.Select((d,i)=>new[] {(i+1).ToString(),d.DriverName}).ToList()));
            var m=scope.Type == SessionType.Qualifying ? [] : SeriesScoring.MainOrder(ev,group.Key);
            if (m.Count>0) report.Sections.Add(new ReportSection($"{cls} • Overall mains",["Position","Driver"],m.Select((d,i)=>new[] {(i+1).ToString(),d.DriverName}).ToList(),"Repeated mains use the configured best runs. Incomplete main groups are excluded."));
            foreach (var s in group.Where(s=>s.ProgressStatus==EventSessionStatus.Completed).OrderBy(s=>s.QueueOrder))
                report.Sections.Add(new ReportSection(s.Name,["Pos","Car","Driver","Laps","Total time","Fast lap"],s.Results.OrderBy(r=>r.Position).Select(r=>new[] {r.Position.ToString(),r.CarNumber.ToString(),r.DriverName,r.Laps.ToString(),r.TotalTime,r.BestLap}).ToList()));
            for (var i = sectionStart; i < report.Sections.Count; i++) report.Sections[i] = report.Sections[i] with { ClassColor = color };
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
    public static RaceReport Practice(string driverName, DateTime start, DateTime end, IEnumerable<(PracticeHistoryRow Stint,IReadOnlyList<PracticeHistoryLapRow> Laps)> stints, RentalTransponderState? rentals = null, IEnumerable<ClassDefinition>? classes = null)
    {
        var report=new RaceReport { Title=$"Practice — {driverName}",Subtitle=$"{start:MM/dd/yyyy}–{end:MM/dd/yyyy}" };
        var rentalLabels = new RentalPrintFormatting(rentals, report);
        foreach (var (stint,laps) in stints.OrderBy(s=>s.Stint.StartedAt))
            report.Sections.Add(new ReportSection($"{stint.DateDisplay} • {stint.TimeDisplay}",["Lap","Lap time (s)","Crossing time"],laps.Select(l=>new[] {l.LapNumber.ToString(),l.LapTimeDisplay,l.TimeDisplay}).ToList(),$"{stint.LapCount} laps • Best {stint.BestLapDisplay} • Average {stint.AverageLapDisplay} • Transponder {rentalLabels.Format(stint.TransponderId)}"));
        return report;
    }
}
