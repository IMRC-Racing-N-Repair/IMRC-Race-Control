using System.Globalization;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using Microsoft.Data.Sqlite;

namespace IMRCRaceControl.Services;

public sealed record LiveTimeImportSource(LiveTimeRoster Roster, IReadOnlyList<LiveTimeEventHistory> History)
{
    public IReadOnlyList<LiveTimeVenue> Venues { get; init; } = [];
    internal IReadOnlyList<LiveTimeEventClassTiming> VenueTimings { get; init; } = [];
    public IReadOnlyList<LiveTimeDriver> HistoryDrivers { get; init; } = [];
    public IReadOnlyList<LiveTimeClass> HistoryClasses { get; init; } = [];
}
public sealed record LiveTimeEventHistory(long LocalId, Guid SourceId, string Name, DateTimeOffset Date,
    DateTimeOffset? End, List<LiveTimeRaceHistory> Races)
{
    public TrackProfile? Track { get; init; }
    public int RaceCount => Races.Count;
    public int LapCount => Races.Sum(r => r.Entries.Sum(e => e.Laps.Count));
    public string Label => $"{Date:yyyy-MM-dd} • {Name} • {RaceCount} races / {LapCount:N0} laps";
}
public sealed record LiveTimeRaceHistory(long LocalId, Guid SourceId, long ClassId, string Name, int Type, int Round,
    int RoundOrder, int Order, int Heat, string MainLetter, int MainLeg, int MainCount, int StartType, int LengthType,
    int LengthSeconds, int LengthLaps, double? MinimumLap, DateTimeOffset? Started, DateTimeOffset? Finished,
    List<LiveTimeRaceEntryHistory> Entries);
public sealed record LiveTimeRaceEntryHistory(long LocalId, long DriverId, int Car, int Position, int CompletedLaps,
    double TotalSeconds, double BestLap, double Average, double Top5, double Top10, double Top15, bool Dns, bool Dnf,
    bool Dq, string Tags, List<LiveTimeRaceLapHistory> Laps);
public sealed record LiveTimeRaceLapHistory(long Sequence, int Number, double Seconds, double Elapsed, DateTimeOffset Time);

public static partial class LiveTimeBackupReader
{
    private static List<LiveTimeEventHistory> ReadRaceHistory(SqliteConnection db, IReadOnlyDictionary<long, TrackProfile> eventTracks)
    {
        var tables = Read(db, "SELECT name FROM sqlite_schema WHERE type='table'", r => r.GetString(0))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!new[] { "Round", "RaceEntry", "EventRegistration", "RaceTransponder" }.All(tables.Contains))
            return [];

        const string raceScope = """
            FROM Race r JOIN [Round] o ON o.LID=r.RoundLID
            JOIN Event e ON e.LID=o.EventLID
            WHERE r.IsActive=1 AND r.IsComplete=1 AND r.TypeID IN (1,2)
                AND o.IsActive=1 AND e.IsActive=1
            """;
        var races = Read(db, """
            SELECT o.EventLID,r.LID,r.GUID,r.RaceClassLID,r.Name,r.TypeID,o.TypeOrderNumber,o.OrderNumber,
                r.OrderNumber,r.RaceClassOrderNumber,r.MainLetter,r.MainNumber,r.MultiMainCount,r.StartTypeID,
                r.LengthTypeID,r.LengthSeconds,r.LengthLaps,r.MinimumLapTimeSeconds,r.StartMicrosecondsUTC,r.EndMicrosecondsUTC
            """ + " " + raceScope, r => (
                EventId:r.GetInt64(0), Race:new LiveTimeRaceHistory(r.GetInt64(1), GuidValue(r,2), Long(r,3), TextValue(r,4),
                    Int(r,5), Int(r,6), Int(r,7), Int(r,8), Int(r,9), TextValue(r,10), Int(r,11), Int(r,12),
                    Int(r,13), Int(r,14), Int(r,15), Int(r,16), r.IsDBNull(17) ? null : Number(r,17),
                    Epoch(Long(r,18)), Epoch(Long(r,19)), [])));
        const string raceIds = "SELECT r.LID " + raceScope;
        var entryScope = $"FROM RaceEntry x WHERE x.RaceLID IN ({raceIds})";
        var tagLookup = Read(db, $"SELECT t.RaceEntryLID,t.TransponderNumber FROM RaceTransponder t " +
            $"WHERE t.TransponderNumber>0 AND t.RaceEntryLID IN (SELECT x.LID {entryScope}) ORDER BY t.LID",
            r => (Entry:r.GetInt64(0), Tag:r.GetInt64(1).ToString(CultureInfo.InvariantCulture)))
            .ToLookup(t => t.Entry,t => t.Tag);
        var laps = Read(db, $"""
            SELECT l.RaceEntryLID,l.LID,l.LapNumber,l.LapTimeSeconds,l.ElapsedTimeSeconds,l.MicrosecondsUTC
            FROM RaceLap l WHERE l.IsValid=1 AND l.LapTypeID=1 AND l.LapNumber>0 AND l.LapTimeSeconds>0
                AND l.RaceEntryLID IN (SELECT x.LID {entryScope})
            ORDER BY l.MicrosecondsUTC,l.LID
            """, r => (Entry:r.GetInt64(0), Lap:new LiveTimeRaceLapHistory(r.GetInt64(1),Int(r,2),Number(r,3),Number(r,4),
                Epoch(Long(r,5)) ?? DateTimeOffset.UnixEpoch)))
            .Where(x => x.Lap.Time > DateTimeOffset.UnixEpoch && x.Lap.Seconds is > 0 and < 86400 && x.Lap.Elapsed is >= 0 and < 604800)
            .ToLookup(x => x.Entry,x => x.Lap);

        var entries = Read(db, $"""
            SELECT x.RaceLID,x.LID,g.DriverLID,x.CarNumber,x.FinalPositionInClass,x.CompletedLaps,x.CompletedTime,
                x.FastestLap,x.AverageLap,x.Top5Average,x.Top10Average,x.Top15Average,x.IsDidNotStart,x.IsDidNotFinish,x.IsDisqualified
            FROM RaceEntry x LEFT JOIN EventRegistration g ON g.LID=x.EventRegistrationLID
            WHERE x.RaceLID IN ({raceIds}) ORDER BY x.OrderNumber,x.LID
            """, r =>
            {
                var id = r.GetInt64(1);
                return (Race:r.GetInt64(0), Entry:new LiveTimeRaceEntryHistory(id, Long(r,2), Int(r,3), Int(r,4),
                    Int(r,5), Number(r,6), Number(r,7), Number(r,8), Number(r,9), Number(r,10), Number(r,11),
                    Int(r,12)!=0, Int(r,13)!=0, Int(r,14)!=0, string.Join(";",tagLookup[id].Distinct()), laps[id].ToList()));
            }).ToLookup(e => e.Race,e => e.Entry);
        var byEvent = races.ToLookup(r => r.EventId, r => r.Race with { Entries = entries[r.Race.LocalId].ToList() });
        return Read(db, "SELECT LID,GUID,Name,StartDateTime,EndDateTime FROM Event WHERE IsActive=1",
                r => new LiveTimeEventHistory(r.GetInt64(0),GuidValue(r,1),TextValue(r,2),Date(TextValue(r,3)) ?? DateTimeOffset.UnixEpoch,
                    Date(TextValue(r,4)),byEvent[r.GetInt64(0)].ToList()) { Track = eventTracks.GetValueOrDefault(r.GetInt64(0)) })
            .Where(e => e.SourceId != Guid.Empty && e.Date > DateTimeOffset.UnixEpoch && (e.Races.Count > 0 || e.End.HasValue))
            .OrderByDescending(e => e.Date).ToList();
    }

    private static long Long(SqliteDataReader r,int i) => r.IsDBNull(i) ? 0 : r.GetInt64(i);
    private static int Int(SqliteDataReader r,int i) => (int)Math.Clamp(Long(r,i),int.MinValue,int.MaxValue);
    private static double Number(SqliteDataReader r,int i) => r.IsDBNull(i) ? 0 : r.GetDouble(i);
    private static DateTimeOffset? Epoch(long microseconds) =>
        microseconds is > 0 and <= 253402300799999999L
            ? DateTimeOffset.UnixEpoch.AddTicks(microseconds*10) : null;
    private static DateTimeOffset? Date(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,out var date) ? date : null;
}

public static class LiveTimeHistoryMerge
{
    public static List<TrackProfile> NewTracks(LiveTimeImportSource source, IEnumerable<string> chosen,
        IEnumerable<TrackProfile> existing, TrackProfile savedTrack)
    {
        var selected = chosen.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var known = existing.Select(t => t.Id).Append(savedTrack.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return source.Venues.Where(v => selected.Contains(v.Id) && v.Track is not null)
            .Select(v => ResolveTrack(source, v.Track, savedTrack)!)
            .Where(t => known.Add(t.Id)).ToList();
    }

    public static string EventId(Guid id) => "livetime-event-" + id.ToString("N");

    public static List<RaceDayEventDefinition> Preview(LiveTimeImportSource source, IEnumerable<Guid> chosen,
        IReadOnlyCollection<RaceDayEventDefinition> existing, LiveTimeRosterPlan roster, bool includeLaps,
        TrackProfile? savedTrack = null)
    {
        var selection = chosen.ToHashSet();
        var eventIds = existing.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var drivers = source.Roster.Drivers.Concat(source.HistoryDrivers).ToDictionary(d => d.LocalId);
        var classes = source.Roster.Classes.Concat(source.HistoryClasses).ToDictionary(c => c.LocalId);
        var output = new List<RaceDayEventDefinition>();
        foreach (var history in source.History.Where(h => selection.Contains(h.SourceId)))
        {
            if (!eventIds.Add(EventId(history.SourceId))) continue;
            var ev = new RaceDayEventDefinition
            {
                Id=EventId(history.SourceId), Name=string.IsNullOrWhiteSpace(history.Name) ? "LiveTime event" : history.Name,
                EventDate=history.Date.Date, CreatedAt=history.Date, Track=ResolveTrack(source, history.Track, savedTrack),
                CompletedAt=history.End ?? history.Races.Select(r => r.Finished).DefaultIfEmpty().Max(), Status=EventStatus.Completed
            };
            var sessionIds = new HashSet<Guid>();
            foreach (var race in history.Races.OrderBy(r => r.RoundOrder).ThenBy(r => r.Order).ThenBy(r => r.LocalId))
            {
                if (race.SourceId == Guid.Empty || !sessionIds.Add(race.SourceId) ||
                    race.Entries.Any(e => e.Car < 1) || race.Entries.Select(e => e.Car).Distinct().Count()!=race.Entries.Count)
                    continue;
                var cls = classes.GetValueOrDefault(race.ClassId);
                var classId = roster.ClassIds.GetValueOrDefault(race.ClassId) ??
                    (cls is not null && cls.SourceId != Guid.Empty ? "livetime-class-" + cls.SourceId.ToString("N")
                        : ev.Id + "-class-" + race.ClassId);
                var className = cls?.Name ?? "LiveTime class";
                var session = new EventSessionDefinition
                {
                    Id="livetime-race-" + race.SourceId.ToString("N"), Name=race.Name, ClassId=classId, ClassName=className,
                    ProgressStatus=EventSessionStatus.Completed, SessionType=race.Type==2 ? SessionType.Race : SessionType.Qualifying,
                    StartStyle=race.StartType==1 ? StartStyle.IndividualCrossing : StartStyle.Tone,
                    RaceFormat=race.LengthType==2 ? RaceFormat.Laps : RaceFormat.Timed,
                    RaceMinutesOverride=race.LengthSeconds>0 ? race.LengthSeconds/60.0 : null,
                    MinimumLapSecondsOverride=race.MinimumLap, TargetLaps=Math.Max(1,race.LengthLaps),
                    Round=Math.Max(1,race.Round), Heat=race.Type==2 && race.MainLetter.Length>0 && race.MainLetter[0] is >= 'A' and <= 'Z'
                        ? race.MainLetter[0]-'A'+1 : Math.Max(1,race.Heat),
                    MainLeg=Math.Max(1,race.MainLeg), MainRunCount=Math.Max(1,race.MainCount),
                    QueueOrder=ev.Sessions.Count+1, RaceNumber=race.Order>0 ? race.Order : ev.Sessions.Count+1,
                    StartedAt=race.Started, CompletedAt=race.Finished
                };
                foreach (var entry in race.Entries)
                {
                    var driver = drivers.GetValueOrDefault(entry.DriverId);
                    var driverId = roster.DriverIds.GetValueOrDefault(entry.DriverId) ??
                        (driver is not null && driver.SourceId != Guid.Empty ? "livetime-driver-" + driver.SourceId.ToString("N")
                            : ev.Id + "-driver-" + entry.DriverId);
                    var entrant = new SessionEntrant
                    {
                        CarNumber=entry.Car, DriverId=driverId, DriverName=driver is null ? "Unknown driver" :
                            $"{driver.FirstName} {driver.LastName}".Trim(), Nickname=driver?.Nickname ?? "", TransponderId=entry.Tags
                    };
                    session.Entrants.Add(entrant);
                    var last = entry.Laps.LastOrDefault();
                    session.Results.Add(new StandingRow
                    {
                        Position=entry.Position, CarNumber=entry.Car, DriverName=entrant.DisplayName, Laps=entry.CompletedLaps,
                        TotalTime=entry.TotalSeconds>0 && entry.TotalSeconds<604800
                            ? RaceEngine.FormatElapsed(TimeSpan.FromSeconds(entry.TotalSeconds)) : "—",
                        LastLap=Lap(last?.Seconds ?? 0), BestLap=Lap(entry.BestLap), AverageLap=Lap(entry.Average),
                        Average5=Lap(entry.Top5), Average10=Lap(entry.Top10), Average15=Lap(entry.Top15),
                        Status=entry.Dq ? "DQ" : entry.Dns ? "DNS" : entry.Dnf ? "DNF" : "FINISHED"
                    });
                    if (includeLaps)
                    {
                        foreach (var lap in entry.Laps)
                            session.Laps.Add(new(lap.Sequence,entry.Car,entrant.DisplayName,lap.Number,TimeSpan.FromSeconds(lap.Seconds),
                                TimeSpan.FromSeconds(lap.Elapsed),lap.Time,"LiveTime",lap==last && !entry.Dnf && !entry.Dns && !entry.Dq));
                    }
                }
                session.Laps = session.Laps.OrderBy(l => l.Timestamp).ThenBy(l => l.Sequence).ToList();
                if (!ev.ClassIds.Contains(classId)) ev.ClassIds.Add(classId);
                ev.Sessions.Add(session);
            }
            if (ev.Sessions.Count > 0 || (history.Races.Count == 0 && history.End.HasValue)) output.Add(ev);
        }
        return output;
    }

    public static List<(RaceDayEventDefinition Event, TrackProfile Track)> MissingTrackLinks(LiveTimeImportSource source,
        IEnumerable<Guid> chosen, IEnumerable<RaceDayEventDefinition> existing, TrackProfile? savedTrack = null)
    {
        var selection = chosen.ToHashSet();
        var history = source.History.Where(h => h.Track is not null && selection.Contains(h.SourceId))
            .ToDictionary(h => EventId(h.SourceId), StringComparer.OrdinalIgnoreCase);
        return existing.Where(e => e.Track is null && history.ContainsKey(e.Id))
            .Select(e => (e, ResolveTrack(source, history[e.Id].Track, savedTrack)!)).ToList();
    }

    internal static TrackProfile? ResolveTrack(LiveTimeImportSource source, TrackProfile? imported, TrackProfile? saved)
    {
        if (imported is null) return null;
        if (saved is not null && string.Equals(imported.Name.Trim(), saved.Name.Trim(), StringComparison.OrdinalIgnoreCase) &&
            source.Venues.Select(v => v.Track).Concat(source.History.Select(e => e.Track))
                .Where(t => string.Equals(t?.Name.Trim(), imported.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(t => t!.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            return saved.Snapshot();
        return imported.Snapshot();
    }

    private static string Lap(double seconds) => seconds is > 0 and < 86400
        ? RaceEngine.FormatLap(TimeSpan.FromSeconds(seconds)) : "—";
}
