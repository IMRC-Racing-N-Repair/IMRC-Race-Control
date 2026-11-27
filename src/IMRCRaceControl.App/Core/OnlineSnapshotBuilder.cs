using System.Text.Json;
using System.Text.Json.Serialization;
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
    // Keep unknown/inapplicable optional race fields absent rather than sending null or invented values.
    private static readonly JsonSerializerOptions EventJson = new(Json) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public static string PracticeDate(TrackProfile track, DateTimeOffset start)
    {
        // Group by the venue's calendar, never by the server's UTC date.
        var zone=TimeZoneInfo.FindSystemTimeZoneById(track.TimeZoneId);
        return TimeZoneInfo.ConvertTime(start,zone).ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
    }
    public static JsonElement CapturePracticeArchive(PracticeHistoryRow row, IReadOnlyList<PracticeHistoryLapRow> laps)
    {
        return JsonSerializer.SerializeToElement(new {
            id=row.StintId, date=row.PracticeDate, name=row.DriverName,
            transponderId=row.TransponderId, startedAt=row.StartedAt.ToUniversalTime().ToString("O"),
            endedAt=row.EndedAt.ToUniversalTime().ToString("O"),
            laps=row.LapCount, best=row.BestLapSeconds, average=row.AverageLapSeconds,
            lapTimes=laps.OrderBy(l=>l.LapNumber).Select(l=>l.LapTimeSeconds).ToArray()
        },Json);
    }
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
        IEnumerable<LapRecord> liveLaps, bool showLive, bool completedOnly = false, RaceTimingSettings? liveSettings = null)
    {
        var all = allEvents.ToList();
        var events = all.Where(e => e.Track?.Id == track.Id).ToList();
        var current = events.FirstOrDefault(e => e.Id == currentEventId);
        var rows = liveRows.ToList(); var laps = liveLaps.ToList();
        var documents = new List<OnlineDocument>();
        foreach (var ev in events)
        {
            var races = ev.Sessions.Where(s => s.SessionType != SessionType.Practice && (!completedOnly || s.ProgressStatus == EventSessionStatus.Completed))
                .OrderBy(s => s.ScheduleStageOrder).ThenBy(s => s.ScheduleRoundOrder).ThenBy(s => s.RaceNumber).ThenBy(s => s.QueueOrder)
                .Select(s => {
                    var combined=CombinedRaces.Run(ev.Sessions,s);
                    int? RuntimeCar(int car) => combined.IsCombinedRace ? combined.Entrants.FirstOrDefault(e=>e.SourceSessionId==s.Id && e.SourceCarNumber==car)?.CarNumber : car;
                    var engine = !completedOnly && showLive && ev.Id == currentEventId && combined.Id == loadedSessionId &&
                        state is RaceState.Armed or RaceState.Running or RaceState.TimeExpired or RaceState.Complete;
                    var completed = s.ProgressStatus == EventSessionStatus.Completed || engine && state == RaceState.Complete;
                    var status = s.ProgressStatus == EventSessionStatus.Skipped ? "skipped" : completed ? "completed" : engine ? "live" : "planned";
                    // Use the actual engine settings for a running/just-finished combined run,
                    // including live format changes. Saved completions retain their own settings.
                    var configuredSession = completed && !engine ? s : combined;
                    var cls = classes.FirstOrDefault(c => c.Id == configuredSession.ClassId);
                    var settings = engine && liveSettings.HasValue
                        ? liveSettings.Value : RaceTimingSettings.ForSession(configuredSession, cls);
                    var timing = engine ? rows : s.Results;
                    var lapData = engine ? laps : s.Laps;
                    var ordered = status is "completed" or "live"
                        ? s.Entrants.OrderBy(e => timing.FirstOrDefault(r => r.CarNumber == (engine ? RuntimeCar(e.CarNumber) : e.CarNumber))?.Position ?? int.MaxValue)
                        : s.Entrants.OrderBy(e => e.CarNumber);
                    return new {
                        id = s.Id, number = s.RaceNumber, round = s.RoundLabel, classId = s.ClassId, className = s.ClassName,
                        combinedRaceId=s.CombinedRaceId, combinedRaceName=combined.IsCombinedRace ? combined.Name : "", classColor=classes.FirstOrDefault(c=>c.Id==s.ClassId)?.PrintColor ?? "#808080",
                        heat = s.SessionType == SessionType.Race ? s.HeatMainDisplay + "-Main" : "Heat " + s.Heat,
                        status, raceFormat = settings.FormatName, durationSeconds = settings.DurationSeconds, targetLaps = settings.LapTarget,
                        drivers = ordered.Select(e => {
                            var timingCar = engine ? RuntimeCar(e.CarNumber) : e.CarNumber;
                            var hasTiming = status is "completed" or "live";
                            var row = hasTiming ? timing.FirstOrDefault(r => r.CarNumber == timingCar) : null;
                            var driverLaps = row is null ? Array.Empty<LapRecord>() : lapData.Where(l => l.CarNumber == timingCar)
                                .OrderBy(l => l.LapNumber).ToArray();
                            return new { name = e.DisplayName, carNumber = e.CarNumber, position = row?.Position ?? e.CarNumber,
                                laps = row?.Laps ?? 0, time = row?.TotalTime ?? "—", best = row?.BestLap ?? "—",
                                lastLap = row?.LastLap ?? "—", gap = row?.GapToLeader ?? "—", finished = row?.IsFinished ?? false,
                                pace = hasTiming ? (row is not null && row.Laps == driverLaps.Length
                                    ? RaceEngine.CalculatePace(driverLaps, settings) : "—") : null,
                                lapTimes = driverLaps.Select(l => l.LapTime.TotalSeconds).ToArray() };
                        }).ToArray()
                    };
                }).ToArray();
            var value = JsonSerializer.SerializeToElement(new { id = ev.Id, name = ev.Name,
                date = ev.EventDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                status = ev.Status == EventStatus.Completed ? "completed" : ev.Id == currentEventId ? "active" : "upcoming", races }, EventJson);
            documents.Add(new("event", ev.Id, value, !completedOnly && ev.Id == currentEventId));
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
