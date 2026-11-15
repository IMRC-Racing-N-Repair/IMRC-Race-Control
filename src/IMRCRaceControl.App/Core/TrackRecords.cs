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
        var sessions = events.Where(e => e.Track?.Id == track.Id)
            .SelectMany(e => e.Sessions.Where(s => s.ProgressStatus == EventSessionStatus.Completed &&
                (s.SessionType == SessionType.Qualifying || s.SessionType == SessionType.Race)).Select(s => (Event:e, Session:s))).ToList();
        var venueClassIds = sessions.Select(x => x.Session.ClassId).ToHashSet();
        var catalog = classes.Where(c => includeUnracedClasses || venueClassIds.Contains(c.Id))
            .Select(c => (c.Id,c.Name,Color:c.PrintColor))
            .Concat(sessions.Select(x => (Id:x.Session.ClassId,Name:x.Session.ClassName,Color:"#DCE1E6")))
            .DistinctBy(c => c.Id).Where(c => classId is null || c.Id == classId).OrderBy(c => c.Name).ThenBy(c=>c.Id);
        return catalog.Select(c =>
        {
            var best = sessions.Where(x => x.Session.ClassId == c.Id).SelectMany(x => x.Session.Laps
                .Where(EligibleLap)
                .Select(l => (Lap:l,x.Event,Driver:x.Session.Entrants.FirstOrDefault(d => d.CarNumber == l.CarNumber),Session:x.Session)))
                .GroupBy(x => !string.IsNullOrWhiteSpace(x.Driver?.DriverId) ? "driver:"+x.Driver.DriverId : $"unknown:{x.Event.Id}:{x.Session.Id}:{x.Lap.CarNumber}")
                .Select(g => g.OrderBy(x=>x.Lap.LapTime).ThenBy(x=>x.Event.EventDate).ThenBy(x=>x.Lap.Timestamp).First())
                .OrderBy(x=>x.Lap.LapTime).ThenBy(x=>x.Event.EventDate).ThenBy(x=>x.Lap.DriverName,StringComparer.Ordinal).Take(count)
                .Select((x,i)=>new TrackRecordRow(i+1,x.Lap.DriverName,x.Lap.LapTime,x.Event.EventDate,x.Event.Name)).ToList();
            return new TrackRecordClass(c.Id,c.Name,c.Color,best);
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
