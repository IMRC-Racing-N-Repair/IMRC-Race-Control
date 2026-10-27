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
        IEnumerable<ClassDefinition> classes, int count = 10, string? classId = null)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        var sessions = events.Where(e => e.Track?.Id == track.Id)
            .SelectMany(e => e.Sessions.Where(s => s.ProgressStatus == EventSessionStatus.Completed &&
                (s.SessionType == SessionType.Qualifying || s.SessionType == SessionType.Race)).Select(s => (Event:e, Session:s))).ToList();
        var catalog = classes.Select(c => (c.Id,c.Name,Color:c.PrintColor))
            .Concat(sessions.Select(x => (Id:x.Session.ClassId,Name:x.Session.ClassName,Color:"#DCE1E6")))
            .DistinctBy(c => c.Id).Where(c => classId is null || c.Id == classId).OrderBy(c => c.Name).ThenBy(c=>c.Id);
        return catalog.Select(c =>
        {
            var best = sessions.Where(x => x.Session.ClassId == c.Id).SelectMany(x => x.Session.Laps
                .Where(l => l.LapNumber > 0 && l.LapTime > TimeSpan.Zero)
                .Select(l => (Lap:l,x.Event,Driver:x.Session.Entrants.FirstOrDefault(d => d.CarNumber == l.CarNumber),Session:x.Session)))
                .GroupBy(x => !string.IsNullOrWhiteSpace(x.Driver?.DriverId) ? "driver:"+x.Driver.DriverId : $"unknown:{x.Event.Id}:{x.Session.Id}:{x.Lap.CarNumber}")
                .Select(g => g.OrderBy(x=>x.Lap.LapTime).ThenBy(x=>x.Event.EventDate).ThenBy(x=>x.Lap.Timestamp).First())
                .OrderBy(x=>x.Lap.LapTime).ThenBy(x=>x.Event.EventDate).ThenBy(x=>x.Lap.DriverName,StringComparer.Ordinal).Take(count)
                .Select((x,i)=>new TrackRecordRow(i+1,x.Lap.DriverName,x.Lap.LapTime,x.Event.EventDate,x.Event.Name)).ToList();
            return new TrackRecordClass(c.Id,c.Name,c.Color,best);
        }).ToList();
    }

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
