using System.Globalization;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

public sealed record TrackRecordRow(int Rank, string Driver, TimeSpan Time, DateTime Date, string Event)
{
    public string TimeDisplay => Time.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);
    public string DateDisplay => Date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
}
public sealed record TrackRecordClass(string Id, string Name, string Color, List<TrackRecordRow> Records);

public static class TrackRecords
{
    // Use stable track/class/driver IDs. Never pool unrelated venues or same-name drivers.
    public static List<TrackRecordClass> Calculate(TrackProfile track, IEnumerable<RaceDayEventDefinition> events,
        IEnumerable<ClassDefinition> classes, int count = 10, string? classId = null, bool includeUnracedClasses = true)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));

        // Walk each historic lap only once, not once per class. Imported
        // databases can contain millions of laps across hundreds of classes.
        // Keep just the driver's best valid lap for each track/class; ties
        // preserve the earlier event date then recorded lap timestamp.
        var bestByClass = new Dictionary<string, Dictionary<string,
            (LapRecord Lap, RaceDayEventDefinition Event)>>(StringComparer.OrdinalIgnoreCase);
        var discovered = new Dictionary<string, (string Name, string Color)>(StringComparer.OrdinalIgnoreCase);
        foreach (var ev in events)
        {
            if (!string.Equals(ev.Track?.Id, track.Id, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var session in ev.Sessions)
            {
                if (session.ProgressStatus != EventSessionStatus.Completed ||
                    session.SessionType is not (SessionType.Qualifying or SessionType.Race) ||
                    string.IsNullOrWhiteSpace(session.ClassId)) continue;

                if (!discovered.ContainsKey(session.ClassId))
                    discovered[session.ClassId] = (session.ClassName, "#DCE1E6");
                if (classId is not null && !string.Equals(session.ClassId, classId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!bestByClass.TryGetValue(session.ClassId, out var records))
                    bestByClass[session.ClassId] = records =
                        new Dictionary<string,(LapRecord,RaceDayEventDefinition)>(StringComparer.OrdinalIgnoreCase);
                var entrants = session.Entrants.ToDictionary(e => e.CarNumber);
                foreach (var lap in session.Laps)
                {
                    if (!EligibleLap(lap)) continue;
                    var driverKey = entrants.TryGetValue(lap.CarNumber, out var driver) &&
                        !string.IsNullOrWhiteSpace(driver.DriverId)
                        ? "driver:" + driver.DriverId
                        : $"unknown:{ev.Id}:{session.Id}:{lap.CarNumber}";
                    if (records.TryGetValue(driverKey, out var prior) &&
                        (prior.Lap.LapTime < lap.LapTime ||
                         prior.Lap.LapTime == lap.LapTime &&
                         (prior.Event.EventDate < ev.EventDate ||
                          prior.Event.EventDate == ev.EventDate &&
                          prior.Lap.Timestamp <= lap.Timestamp))) continue;
                    records[driverKey] = (lap, ev);
                }
            }
        }

        var catalog = classes.Where(c => includeUnracedClasses || discovered.ContainsKey(c.Id))
            .Select(c => (c.Id, c.Name, Color:c.PrintColor))
            .Concat(discovered.Select(k => (Id:k.Key,Name:k.Value.Name,Color:k.Value.Color)))
            .DistinctBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .Where(c => classId is null || string.Equals(c.Id, classId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name).ThenBy(c => c.Id);

        return catalog.Select(c =>
        {
            var records = bestByClass.TryGetValue(c.Id, out var matches)
                ? matches.Values.OrderBy(x => x.Lap.LapTime)
                    .ThenBy(x => x.Event.EventDate)
                    .ThenBy(x => x.Lap.DriverName,StringComparer.Ordinal)
                    .Take(count)
                    .Select((x,i) => new TrackRecordRow(i+1,x.Lap.DriverName,x.Lap.LapTime,x.Event.EventDate,x.Event.Name))
                    .ToList()
                : new List<TrackRecordRow>();
            return new TrackRecordClass(c.Id,c.Name,c.Color,records);
        }).ToList();
    }

    public static Dictionary<string, TimeSpan> BestTimes(string trackId, IEnumerable<RaceDayEventDefinition> events,
        IReadOnlySet<string> excludedRaces)
    {
        return events.Where(e => e.Track?.Id == trackId).SelectMany(e => e.Sessions)
            .Where(s => !excludedRaces.Contains(s.Id) && s.ProgressStatus == EventSessionStatus.Completed &&
                s.SessionType is SessionType.Qualifying or SessionType.Race && !string.IsNullOrWhiteSpace(s.ClassId))
            .SelectMany(s => s.Laps.Where(EligibleLap).Select(l => (s.ClassId, l.LapTime)))
            .GroupBy(l => l.ClassId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Min(l => l.LapTime), StringComparer.OrdinalIgnoreCase);
    }

    internal static bool EligibleLap(LapRecord lap) => lap.LapNumber > 0 && lap.LapTime > TimeSpan.Zero;

    public static RaceReport Report(string trackName, IReadOnlyList<TrackRecordClass> classes, int count)
    {
        var report = new RaceReport { Title="Track records", Subtitle=$"{trackName} • Top {count} per class • Best lap per driver",
            EmptyMessage="No classes available. Add a class to start tracking records." };
        foreach(var c in classes)
            report.Sections.Add(new ReportSection(c.Name,["Rank","Driver","Lap time (s)","Event date","Event"],
                c.Records.Select(r=>new[]{r.Rank.ToString(),r.Driver,r.TimeDisplay,r.DateDisplay,r.Event}).ToList(),
                c.Records.Count==0 ? "No completed race laps recorded for this class at this track." : "Completed qualifying and mains • Valid laps only",ClassColor:c.Color));
        return report;
    }
}

public sealed record RaceTrackRecord(string Driver, string ClassName, TimeSpan Time)
{
    public string Banner => $"NEW TRACK RECORD  •  {ClassName}  •  {Driver}  •  {RaceEngine.FormatLap(Time)} s";
    public string Announcement(string spokenName) =>
        $"New track record for {ClassName}. {spokenName}, {Time.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)} seconds.";
}

public sealed class TrackRecordRace(IReadOnlyDictionary<string, TimeSpan> previous)
{
    private readonly Dictionary<string, TimeSpan> _leaders = new(previous, StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _finished = [];
    public RaceTrackRecord? Latest { get; private set; }

    public void Reset()
    {
        Latest = null;
        _finished.Clear();
        _leaders.Clear();
        foreach (var item in previous) _leaders.Add(item.Key, item.Value);
    }

    public RaceTrackRecord? DriverFinished(LapRecord finish, string classId, string className, IEnumerable<LapRecord> accepted)
    {
        if (!finish.Finished || !_finished.Add(finish.CarNumber) || !_leaders.TryGetValue(classId, out var record))
            return null;
        var fastest = accepted.Where(l => l.CarNumber == finish.CarNumber && TrackRecords.EligibleLap(l))
            .Select(l => l.LapTime).DefaultIfEmpty(TimeSpan.MaxValue).Min();
        if (fastest >= record) return null;
        _leaders[classId] = fastest;
        return Latest = new(finish.DriverName, className, fastest);
    }
}
