using IMRCRaceControl.Models;
namespace IMRCRaceControl.Core;

public sealed record PrintReportChoice(int ReportType, EventReportScope Scope, string Label)
{
    // ReportType retains the existing dispatch: 0 practice, 1 results, 2 lineup, 3 series.
    public static List<PrintReportChoice> Available(RaceDayEventDefinition? raceEvent, string? classId, bool practiceAvailable, bool seriesAvailable)
    {
        var choices = new List<PrintReportChoice>();
        if (raceEvent is not null)
        {
            var sessions = raceEvent.Sessions.Where(s => (classId is null || s.ClassId == classId) &&
                s.SessionType is SessionType.Qualifying or SessionType.Race).ToList();
            var scopes = EventReportScope.Available(raceEvent).Where(scope => sessions.Any(scope.Matches))
                .OrderBy(scope => scope.Type is null ? 1 : 0).ToList();
            foreach (var scope in scopes)
            {
                choices.Add(new(2,scope,$"{scope.Label} — Lineup"));
                var runs = sessions.Where(scope.Matches).Where(s => s.ProgressStatus != EventSessionStatus.Skipped).ToList();
                if (runs.Count > 0 && runs.All(s => s.ProgressStatus == EventSessionStatus.Completed) && runs.Any(s => s.Results.Count > 0))
                    choices.Add(new(1,scope,$"{scope.Label} — Completed results"));
            }
        }
        if (practiceAvailable) choices.Add(new(0,new(),"Practice — Driver report"));
        if (seriesAvailable) choices.Add(new(3,new(),"Series — Standings"));
        return choices;
    }
}
