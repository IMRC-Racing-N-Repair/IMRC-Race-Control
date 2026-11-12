using IMRCRaceControl.Models;
using Microsoft.Data.Sqlite;

namespace IMRCRaceControl.Services;

public sealed record LiveTimeVenue(TrackProfile? Track, IReadOnlySet<long> EventIds,
    IReadOnlySet<(long Driver, long Class)> Registrations, IReadOnlySet<long> ClassIds)
{
    public string Id => Track?.Id ?? "";
    public string Name => Track?.Name ?? "Events without a track";
    public string Label => $"{Name}  •  {EventIds.Count:N0} events";
}

internal sealed record LiveTimeEventClassTiming(long EventId, long ClassId, LiveTimeClassTiming Timing);

public static partial class LiveTimeBackupReader
{
    private static (List<LiveTimeVenue> Venues, Dictionary<long, TrackProfile> EventTracks,
        List<LiveTimeEventClassTiming> Timings) ReadVenues(SqliteConnection db)
    {
        var tables = Read(db, "SELECT name FROM sqlite_schema WHERE type='table'", r => r.GetString(0))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool HasColumns(string table, params string[] required)
        {
            if (!tables.Contains(table)) return false;
            var columns = Read(db, $"PRAGMA table_info('{table}')", r => r.GetString(1))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return required.All(columns.Contains);
        }

        if (!HasColumns("Track", "LID", "GUID", "Name")) return ([], [], []);
        var tracks = Read(db, "SELECT LID,GUID,Name FROM Track", r => (Id: Long(r,0), Guid: GuidValue(r,1), Name: TextValue(r,2)))
            .Where(t => t.Guid != Guid.Empty && t.Name.Length > 0)
            .ToDictionary(t => t.Id, t => new TrackProfile { Id = "livetime-track-" + t.Guid.ToString("N"), Name = t.Name });
        if (tracks.Count == 0) return ([], [], []);

        var trackColumn = HasColumns("Event", "TrackLID") ? "TrackLID" : "NULL";
        var events = Read(db, $"SELECT LID,{trackColumn} FROM Event WHERE IsActive=1",
            r => (Id: Long(r,0), Track: tracks.GetValueOrDefault(Long(r,1))));
        var byTrack = events.ToLookup(e => e.Track?.Id ?? "");
        var eventTracks = events.Where(e => e.Track is not null).ToDictionary(e => e.Id, e => e.Track!);
        var timings = tables.Contains("EventRaceClassSetting") ? ReadEventClassTimings(db) : [];
        var timingClasses = timings.ToLookup(x => x.EventId, x => x.ClassId);

        var registrations = new HashSet<(long Event, long Driver, long Class)>();
        if (HasColumns("EventRegistration", "EventLID", "DriverLID", "RaceClassLID"))
        {
            var registered = HasColumns("EventRegistration", "IsRegistered") ? " AND COALESCE(g.IsRegistered,1)<>0" : "";
            registrations.UnionWith(Read(db, """
                SELECT DISTINCT g.EventLID,g.DriverLID,g.RaceClassLID
                FROM EventRegistration g JOIN Event e ON e.LID=g.EventLID
                WHERE e.IsActive=1 AND g.DriverLID>0 AND g.RaceClassLID>0
                """ + registered, r => (Long(r,0), Long(r,1), Long(r,2))));
        }
        // A saved race entry also proves membership, even in backups with minimal registration columns.
        List<(long Event, long Class)> raceClasses = [];
        if (HasColumns("Round", "EventLID", "IsActive") && HasColumns("Race", "RoundLID", "RaceClassLID", "IsActive"))
        {
            const string active = " FROM Race r JOIN [Round] o ON o.LID=r.RoundLID JOIN Event e ON e.LID=o.EventLID ";
            const string where = " WHERE e.IsActive=1 AND o.IsActive=1 AND r.IsActive=1 AND r.RaceClassLID>0";
            raceClasses = Read(db, "SELECT DISTINCT o.EventLID,r.RaceClassLID" + active + where, r => (Long(r,0), Long(r,1)));
            if (HasColumns("RaceEntry", "RaceLID", "EventRegistrationLID") && HasColumns("EventRegistration", "LID", "DriverLID"))
                registrations.UnionWith(Read(db, "SELECT DISTINCT o.EventLID,g.DriverLID,r.RaceClassLID" + active +
                    " JOIN RaceEntry x ON x.RaceLID=r.LID JOIN EventRegistration g ON g.LID=x.EventRegistrationLID" +
                    where + " AND g.DriverLID>0", r => (Long(r,0), Long(r,1), Long(r,2))));
        }
        var registrationLookup = registrations.ToLookup(r => r.Event, r => (r.Driver, r.Class));
        var raceClassLookup = raceClasses.ToLookup(r => r.Event, r => r.Class);

        LiveTimeVenue Venue(TrackProfile? track)
        {
            var ids = byTrack[track?.Id ?? ""].Select(e => e.Id).ToHashSet();
            var entries = ids.SelectMany(id => registrationLookup[id]).ToHashSet();
            var classes = entries.Select(e => e.Class).Concat(ids.SelectMany(id => timingClasses[id]))
                .Concat(ids.SelectMany(id => raceClassLookup[id])).ToHashSet();
            return new(track, ids, entries, classes);
        }
        var venues = tracks.Values.DistinctBy(t => t.Id).OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(Venue).ToList();
        if (byTrack[""].Any()) venues.Add(Venue(null));
        return (venues, eventTracks, timings);
    }

    private static List<LiveTimeEventClassTiming> ReadEventClassTimings(SqliteConnection db) => Read(db, """
        SELECT s.EventLID,s.RaceClassLID,s.MinimumLapTimeSeconds,s.QualifyingStartTypeID,s.QualifyingLengthTypeID,
            s.QualifyingLengthSeconds,s.QualifyingLengthLaps,s.QualifyingNumberOfDriversPerRace,
            s.MainStartTypeID,s.MainLengthTypeID,s.MainDefaultMainLengthSeconds,s.MainDefaultMainLengthLaps,
            s.MainDefaultNumberOfDrivers
        FROM EventRaceClassSetting s JOIN Event e ON e.LID=s.EventLID
        WHERE e.IsActive=1
        ORDER BY julianday(e.StartDateTime),e.LID,s.LID
        """, r => new LiveTimeEventClassTiming(Long(r,0), Long(r,1), new(
            r.IsDBNull(2) ? null : Number(r,2), Int(r,3), Int(r,4), Int(r,5), Int(r,6), Int(r,7),
            Int(r,8), Int(r,9), Int(r,10), Int(r,11), Int(r,12))));
}

public static class LiveTimeVenueSelection
{
    public static LiveTimeImportSource Scope(LiveTimeImportSource source, IReadOnlySet<string> selected)
    {
        if (source.Venues.Count == 0) return source;
        var venues = source.Venues.Where(v => selected.Contains(v.Id)).ToList();
        var events = venues.SelectMany(v => v.EventIds).ToHashSet();
        var classes = venues.SelectMany(v => v.ClassIds).ToHashSet();
        var registrations = venues.SelectMany(v => v.Registrations).ToHashSet();
        var drivers = registrations.Select(x => x.Driver).ToHashSet();
        var scoped = FilterRoster(source.Roster, drivers, classes);
        var savedEntries = scoped.Entries.ToDictionary(e => (e.DriverId, e.ClassId));
        var availableDrivers = scoped.Drivers.Select(d => d.LocalId).ToHashSet();
        var availableClasses = scoped.Classes.Select(c => c.LocalId).ToHashSet();
        var timings = source.VenueTimings.Where(t => events.Contains(t.EventId))
            .GroupBy(t => t.ClassId).ToDictionary(g => g.Key, g => g.Last().Timing);
        return source with
        {
            History = source.History.Where(e => events.Contains(e.LocalId)).ToList(),
            // Keep definitions for historical results even if someone excludes them from the roster.
            HistoryDrivers = source.HistoryDrivers.Concat(source.Roster.Drivers.Where(d => !availableDrivers.Contains(d.LocalId))).ToList(),
            HistoryClasses = source.HistoryClasses.Concat(source.Roster.Classes.Where(c => !availableClasses.Contains(c.LocalId))).ToList(),
            Roster = scoped with
            {
                Classes = scoped.Classes.Select(c =>
                {
                    var timing = timings.GetValueOrDefault(c.LocalId);
                    return c with { Timing = timing, MinimumLapSeconds = timing?.MinimumLap ?? c.MinimumLapSeconds };
                }).ToList(),
                Entries = registrations.Where(x => availableDrivers.Contains(x.Driver) && availableClasses.Contains(x.Class))
                    .OrderBy(x => x.Driver).ThenBy(x => x.Class)
                    .Select(x => savedEntries.GetValueOrDefault(x) ?? new(x.Driver, x.Class, "")).ToList()
            }
        };
    }

    public static LiveTimeRoster FilterRoster(LiveTimeRoster roster, IReadOnlySet<long>? drivers, IReadOnlySet<long>? classes) =>
        drivers is null && classes is null ? roster : roster with
        {
            SharedTransponderNumbers = LiveTimeRosterMerge.SharedTags(roster),
            Drivers = roster.Drivers.Where(d => drivers is null || drivers.Contains(d.LocalId)).ToList(),
            Classes = roster.Classes.Where(c => classes is null || classes.Contains(c.LocalId)).ToList(),
            Entries = roster.Entries.Where(e => (drivers is null || drivers.Contains(e.DriverId)) &&
                (classes is null || classes.Contains(e.ClassId))).ToList()
        };
}
