using System.Text.Json;
using IMRCRaceControl.Models;
namespace IMRCRaceControl.Core;
public sealed record OnlineDocument(string Kind, string Id, JsonElement Value, bool Heartbeat = false);
public sealed record OnlineCapture(string? CurrentEventId, List<OnlineDocument> Documents, int ExcludedEvents, int ExcludedSeries)
{
    public JsonElement? Practice { get; init; }
}
public static class OnlineSnapshotBuilder
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static JsonElement CapturePractice(bool active, IEnumerable<PracticeCardViewModel> cards, DateTimeOffset now)
    {
        var rows = active ? cards.Where(c => !c.IsSaving && now-c.LastCrossingAt <= c.InactivityTimeout).Take(100).Select(c => new {
            id=c.StintId, name=c.DriverName, laps=c.LapCount, lastLap=c.LastLapDisplay, best=c.BestLapDisplay,
            average=c.AverageLapDisplay, lastCrossingAt=c.LastCrossingAt.ToUniversalTime().ToString("O"),
            lapTimes=c.Laps.TakeLast(1000).Select(l=>l.LapTime.TotalSeconds).ToArray(),
            stats=c.VisibleStats.Select(s=>new {label=s.Label,value=s.Value}).ToArray()
        }).ToArray() : [];
        return JsonSerializer.SerializeToElement(new { id="current",active,drivers=rows }, Json);
    }
    public static OnlineCapture Capture(TrackProfile track, IEnumerable<RaceDayEventDefinition> allEvents,
        IEnumerable<RaceSeriesDefinition> allSeries, IEnumerable<DriverProfile> drivers, IEnumerable<ClassDefinition> classes,
        string? currentEventId, string? loadedSessionId, RaceState state, IEnumerable<StandingRow> liveRows,
        IEnumerable<LapRecord> liveLaps, bool showLive)
    {
        var all = allEvents.ToList();
        var events = all.Where(e => e.Track?.Id == track.Id).ToList();
        var current = events.FirstOrDefault(e => e.Id == currentEventId);
        var rows = liveRows.ToList(); var laps = liveLaps.ToList();
        var documents = new List<OnlineDocument>();
        foreach (var ev in events)
        {
            var races = ev.Sessions.Where(s => s.SessionType != SessionType.Practice)
                .OrderBy(s => s.ScheduleStageOrder).ThenBy(s => s.ScheduleRoundOrder).ThenBy(s => s.RaceNumber).ThenBy(s => s.QueueOrder)
                .Select(s => {
                    var engine = showLive && ev.Id == currentEventId && s.Id == loadedSessionId &&
                        state is RaceState.Armed or RaceState.Running or RaceState.TimeExpired or RaceState.Complete;
                    var completed = s.ProgressStatus == EventSessionStatus.Completed || engine && state == RaceState.Complete;
                    var status = s.ProgressStatus == EventSessionStatus.Skipped ? "skipped" : completed ? "completed" : engine ? "live" : "planned";
                    var timing = engine ? rows : s.Results;
                    var lapData = engine ? laps : s.Laps;
                    var ordered = status is "completed" or "live"
                        ? s.Entrants.OrderBy(e => timing.FirstOrDefault(r => r.CarNumber == e.CarNumber)?.Position ?? int.MaxValue)
                        : s.Entrants.OrderBy(e => e.CarNumber);
                    return new {
                        id = s.Id, number = s.RaceNumber, round = s.RoundLabel, classId = s.ClassId, className = s.ClassName,
                        heat = s.SessionType == SessionType.Race ? s.HeatMainDisplay + "-Main" : "Heat " + s.Heat,
                        status,
                        drivers = ordered.Select(e => {
                            var row = status is "completed" or "live" ? timing.FirstOrDefault(r => r.CarNumber == e.CarNumber) : null;
                            return new { name = e.DisplayName, carNumber = e.CarNumber, position = row?.Position ?? e.CarNumber,
                                laps = row?.Laps ?? 0, time = row?.TotalTime ?? "—", best = row?.BestLap ?? "—",
                                lastLap = row?.LastLap ?? "—", gap = row?.GapToLeader ?? "—", finished = row?.IsFinished ?? false,
                                lapTimes = row is null ? Array.Empty<double>() : lapData.Where(l => l.CarNumber == e.CarNumber)
                                    .OrderBy(l => l.LapNumber).Select(l => l.LapTime.TotalSeconds).ToArray() };
                        }).ToArray()
                    };
                }).ToArray();
            var value = JsonSerializer.SerializeToElement(new { id = ev.Id, name = ev.Name,
                date = ev.EventDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                status = ev.Status == EventStatus.Completed ? "completed" : ev.Id == currentEventId ? "active" : "upcoming", races }, Json);
            documents.Add(new("event", ev.Id, value, ev.Id == currentEventId));
        }
        var excludedSeries = 0;
        foreach (var series in allSeries)
        {
            var selected = SeriesScoring.SelectedEvents(series, all);
            if (!selected.Any(e => e.Track?.Id == track.Id)) continue;
            // Never silently recompute a multi-track championship from only part of its events.
            if (selected.Any(e => e.Track?.Id != track.Id)) { excludedSeries++; continue; }
            foreach (var classId in selected.SelectMany(e => e.ClassIds.Concat(e.Sessions.Select(s => s.ClassId)).Concat(e.DriverAssignments.Select(d => d.ClassId))).Distinct())
            {
                var standings = SeriesScoring.Calculate(series, selected, classId, drivers);
                var id = series.Id + ":" + classId;
                var className = classes.FirstOrDefault(c => c.Id == classId)?.Name ?? selected.SelectMany(e => e.Sessions).FirstOrDefault(s => s.ClassId == classId)?.ClassName ?? classId;
                var value = JsonSerializer.SerializeToElement(new { id, name = series.Name, classId, className,
                    bestOf = series.BestEventCount > 0 ? series.BestEventCount : Math.Max(1, selected.Count),
                    weeks = selected.Select(e => e.EventDate.ToString("MM/dd/yyyy") + " " + e.Name).ToArray(), eventIds = selected.Select(e => e.Id).ToArray(), authoritative = true,
                    drivers = standings.Select(r => new { name = r.DriverName, position = r.Position, total = r.Points, manualPoints = r.ManualPoints,
                        points = r.Weeks.Select(w => w.Pending || !w.Attended && w.Adjustment == 0 ? (decimal?)null : w.Total).ToArray(),
                        counted = r.Weeks.Select((w,i) => (w,i)).Where(x => x.w.Counted).Select(x => x.i).ToArray(),
                        notes = r.Weeks.Select(w => w.Notes).ToArray() }).ToArray() }, Json);
                documents.Add(new("series", id, value));
            }
        }
        return new(current?.Id, documents, all.Count - events.Count, excludedSeries);
    }
}
