using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Services;

/// <summary>
/// Converts RCSP's read-only Advantage tables into the existing selective
/// roster / race history review model. No writes occur during this stage.
/// </summary>
public static class RcspImportReader
{
    private sealed record SourceLap(long Driver, long Class, int Date, int Race,
        int Round, int Type, int Number, double Seconds, double Total, double Epoch);
    private sealed record SourceEntry(RcspAdvantageRow Row, long Track, long Driver,
        long Class, int Date, int Race, int Round, string Heat, string RoundType);
    private sealed record SourceRaceKey(long Track, int Date, int Race, int Round, long Class,
        string Heat, string Type);
    private sealed record SourceLapKey(int Date, int Race, int Round, long Class,
        long Driver, int Type);

    public static LiveTimeImportSource ReadImport(string path)
    {
        if (!File.Exists(path) || !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select an original RC Scoring Pro ZIP backup.");
        using var archive = ZipFile.OpenRead(path);
        RcspAdvantageTable Table(string name) => RcspAdvantageTable.FromZip(archive,name);
        var tracksTable = Table("Tracks");
        var driversTable = Table("Driver");
        var classesTable = Table("Classes");
        var assignmentsTable = Table("DriverClasses");
        var entriesTable = Table("Entries");
        var lapsTable = Table("Practice");
        var rentalsTable = Table("LoanerTransponders");

        static long Id(RcspAdvantageRow r, string column) => r.Integer(column) is > 0 and var value ? value : 0;
        static double Valid(double? number) => number is > 0 and < 86400 ? number.Value : 0;
        static int Julian(DateOnly? date) => date is { } d ? d.DayNumber : -1;
        static Guid Key(string text)
        {
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes("IMRC-RCSP/v1/" + text));
            return new Guid(digest.AsSpan(0, 16));
        }
        static DateTimeOffset Stamp(int day) =>
            new(DateOnly.FromDayNumber(day).ToDateTime(TimeOnly.MinValue),TimeSpan.Zero);
        static (string First, string Last) SplitName(string name)
        {
            var comma = name.IndexOf(',');
            if (comma >= 0)
                return (name[(comma+1)..].Trim(),name[..comma].Trim());
            var parts = name.Split(' ',2,StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2 ? (parts[0],parts[1]) : (name,"");
        }

        var tracks = tracksTable.ActiveRows()
            .Where(r => Id(r,"TrackID") > 0 && !string.IsNullOrWhiteSpace(r.Text("Track")))
            .GroupBy(r => Id(r,"TrackID"))
            .ToDictionary(g=>g.Key,g=>new TrackProfile
            {
                Id="rcsp-track-"+g.Key.ToString(CultureInfo.InvariantCulture),
                Name=g.Last().Text("Track")
            });

        var entries = new List<SourceEntry>();
        var fallbackNames = new Dictionary<long,string>();
        var fallbackClasses = new Dictionary<long,string>();
        foreach (var row in entriesTable.ActiveRows())
        {
            var driver = Id(row,"DriverID");
            var classId = Id(row,"ClassID");
            var track = Id(row,"TrackID");
            var day = Julian(row.Date("Date"));
            var race = (int)Id(row,"RaceNo");
            var round = int.TryParse(row.Text("Round"),out var r) ? r : 0;
            if (driver == 0 || classId == 0 || track == 0 || day < 0) continue;
            if (!string.IsNullOrWhiteSpace(row.Text("FullName")))
                fallbackNames[driver] = row.Text("FullName");
            if (!string.IsNullOrWhiteSpace(row.Text("Class")))
                fallbackClasses[track * 1_000_000 + classId] = row.Text("Class");
            if (race > 0 && round > 0)
                entries.Add(new(row,track,driver,track * 1_000_000+classId,day,race,round,
                    row.Text("Heat"),row.Text("RoundType").ToUpperInvariant()));
        }

        var driversById = driversTable.ActiveRows()
            .Where(r => Id(r,"DriverID") > 0)
            .GroupBy(r => Id(r,"DriverID"))
            .ToDictionary(g=>g.Key,g=>g.Last());
        var rosterDrivers = driversById.Keys.Concat(fallbackNames.Keys).Distinct()
            .Select(id =>
            {
                driversById.TryGetValue(id,out var row);
                var fullName = row?.Text("FullName") ?? "";
                if (string.IsNullOrWhiteSpace(fullName))
                    fullName = fallbackNames.GetValueOrDefault(id) ?? "";
                var (first,last) = SplitName(fullName);
                return new LiveTimeDriver(id,Key("driver/"+id),first,last,row?.Text("NickName") ?? "","");
            })
            .Where(d => !string.IsNullOrWhiteSpace(d.FirstName))
            .OrderBy(d=>d.LastName).ToList();

        var classRows = classesTable.ActiveRows()
            .Where(r => Id(r,"TrackID")>0 && Id(r,"ClassID")>0)
            .GroupBy(r => Id(r,"TrackID")*1_000_000+Id(r,"ClassID"))
            .ToDictionary(g=>g.Key,g=>g.Last());
        var rosterClasses = classRows.Keys.Concat(fallbackClasses.Keys).Distinct()
            .Select(id =>
            {
                classRows.TryGetValue(id,out var row);
                var name = row?.Text("Class") ?? "";
                if (string.IsNullOrWhiteSpace(name)) name=fallbackClasses.GetValueOrDefault(id)??"";
                var min = Valid(row?.Number("MinLap"));
                return new LiveTimeClass(id,Key("class/"+id),name,"",
                    min>0?min:null);
            })
            .Where(c=>!string.IsNullOrWhiteSpace(c.Name))
            .OrderBy(c=>c.Name).ToList();

        var assignments = new Dictionary<(long Driver,long Class),HashSet<string>>();
        void AddTag(long driver,long cls,string value)
        {
            if (driver<=0 || cls<=0 || string.IsNullOrWhiteSpace(value) || value=="0") return;
            var key=(driver,cls);
            if (!assignments.TryGetValue(key,out var tags))
                assignments[key]=tags=new(StringComparer.OrdinalIgnoreCase);
            tags.Add(value);
        }
        foreach (var row in assignmentsTable.ActiveRows())
        {
            var driver=Id(row,"DriverID");
            var cls=Id(row,"TrackID")*1_000_000+Id(row,"ClassID");
            AddTag(driver,cls,Id(row,"Transponder").ToString(CultureInfo.InvariantCulture));
        }
        foreach(var entry in entries)
            AddTag(entry.Driver,entry.Class,
                Id(entry.Row,"Transponder").ToString(CultureInfo.InvariantCulture));
        var rosterEntries=assignments
            .Where(x=>rosterDrivers.Any(d=>d.LocalId==x.Key.Driver) &&
                      rosterClasses.Any(c=>c.LocalId==x.Key.Class))
            .Select(x=>new LiveTimeClassEntry(x.Key.Driver,x.Key.Class,string.Join(";",x.Value)))
            .ToList();

        var laps = new Dictionary<SourceLapKey,List<SourceLap>>();
        foreach (var row in lapsTable.ActiveRows())
        {
            var driver=Id(row,"DriverID");
            var classId=Id(row,"ClassID");
            var day=Julian(row.Date("Date"));
            var race=(int)Id(row,"RaceNo");
            var round=(int)Id(row,"Round");
            var type=(int)Id(row,"LapType");
            var number=(int)Id(row,"Laps");
            var seconds=Valid(row.Number("Difference"));
            if (driver<=0 || classId<=0 || day<0 || race<=0 || round<=0 ||
                type is not (1 or 2) || number<=0 || seconds<=0) continue;
            var key=new SourceLapKey(day,race,round,classId,driver,type);
            if (!laps.TryGetValue(key,out var list)) laps[key]=list=[];
            list.Add(new(driver,classId,day,race,round,type,number,seconds,
                Valid(row.Number("dTotalTime")),row.Number("TransTime") ?? 0));
        }

        var history = new List<LiveTimeEventHistory>();
        long sequence=0;
        foreach (var byDay in entries.GroupBy(x=>(x.Track,x.Date))
                     .OrderBy(g=>g.Key.Date).ThenBy(g=>g.Key.Track))
        {
            var track=tracks.GetValueOrDefault(byDay.Key.Track);
            var races=new List<LiveTimeRaceHistory>();
            foreach (var byRace in byDay.GroupBy(x=>new SourceRaceKey(x.Track,x.Date,x.Race,
                       x.Round,x.Class,x.Heat,x.RoundType))
                     .OrderBy(g=>g.Key.Round).ThenBy(g=>g.Key.Race)
                     .ThenBy(g=>g.Key.Class).ThenBy(g=>g.Key.Heat))
            {
                var key=byRace.Key;
                var sample=byRace.First().Row;
                var isMain=key.Type=="M" ||
                    (key.Type.Length==0 && key.Heat.Length>0 && char.IsLetter(key.Heat[0]));
                var type=isMain?2:1;
                var originalClass=key.Class % 1_000_000;
                var raceEntries=new List<LiveTimeRaceEntryHistory>();
                foreach (var entry in byRace.GroupBy(x=>x.Driver)
                             .Select(g=>g.OrderByDescending(x=>(int)Id(x.Row,"Laps")).First()))
                {
                    var row=entry.Row;
                    var completed=(int)Id(row,"Laps");
                    var matched=laps.GetValueOrDefault(new(entry.Date,entry.Race,entry.Round,
                        originalClass,entry.Driver,type)) ?? [];
                    // Only include lap indices within the scored result; never fabricate
                    // missing laps or import crossings beyond a driver's official total.
                    var selected=matched.Where(x=>x.Number<=completed)
                        .GroupBy(x=>x.Number)
                        .Select(g=>g.OrderByDescending(x=>x.Epoch).First())
                        .OrderBy(x=>x.Number).ToList();
                    double elapsed=0;
                    var raceLaps=new List<LiveTimeRaceLapHistory>();
                    foreach (var lap in selected)
                    {
                        elapsed+=lap.Seconds;
                        DateTimeOffset time;
                        try { time=lap.Epoch>0 && lap.Epoch<253402300799
                            ?DateTimeOffset.FromUnixTimeMilliseconds((long)(lap.Epoch*1000))
                            :Stamp(key.Date); }
                        catch { time=Stamp(key.Date); }
                        raceLaps.Add(new(++sequence,lap.Number,lap.Seconds,
                            lap.Total>0?lap.Total:elapsed,time));
                    }
                    var best=Valid(row.Number("FastLap"));
                    var average=Valid(row.Number("AveLap"));
                    var total=Valid(row.Number("RaceTime"));
                    if (average==0 && completed>0) average=total/completed;
                    raceEntries.Add(new LiveTimeRaceEntryHistory(
                        entry.Driver,entry.Driver,(int)Id(row,"CarNumber"),
                        (int)Id(row,"Position"),completed,total,best,average,
                        Valid(row.Number("AveTop5")),Valid(row.Number("AveTop10")),0,
                        completed==0,false,false,
                        Id(row,"Transponder").ToString(CultureInfo.InvariantCulture),raceLaps));
                }
                // Refuse malformed numbering rather than silently dropping a driver.
                if (raceEntries.Count==0 || raceEntries.Any(e=>e.Car<=0) ||
                    raceEntries.Select(e=>e.Car).Distinct().Count()!=raceEntries.Count)
                    continue;
                var letter=isMain&&key.Heat.Length>0&&char.IsLetter(key.Heat[0])
                    ?char.ToUpperInvariant(key.Heat[0]).ToString():"";
                var mainLeg=isMain&&key.Heat.Length>1&&int.TryParse(key.Heat[1..],out var leg)
                    ?leg:1;
                var lengthSeconds=(int)Id(sample,"RaceLengthSeconds");
                var lengthLaps=(int)Id(sample,"RaceLengthLaps");
                var min=classRows.GetValueOrDefault(key.Class)?.Number("MinLap");
                races.Add(new LiveTimeRaceHistory(races.Count+1,
                    Key($"race/{key.Track}/{key.Date}/{key.Race}/{key.Round}/{key.Class}/{key.Heat}/{key.Type}"),
                    key.Class,
                    $"{rosterClasses.FirstOrDefault(c=>c.LocalId==key.Class)?.Name??"RCSP class"} {key.Type}{key.Round} {key.Heat}",
                    type,key.Round,key.Round,key.Race,
                    int.TryParse(key.Heat,out var heat)?heat:1,
                    letter,mainLeg,1,2,lengthLaps>0?2:1,lengthSeconds,lengthLaps,
                    Valid(min)>0?Valid(min):null,null,null,raceEntries));
            }
            if (races.Count==0) continue;
            var name=$"RC Scoring Pro • {track?.Name??"Track "+byDay.Key.Track} • {Stamp(byDay.Key.Date):yyyy-MM-dd}";
            history.Add(new LiveTimeEventHistory(history.Count+1,
                Key($"event/{byDay.Key.Track}/{byDay.Key.Date}"),name,
                Stamp(byDay.Key.Date),Stamp(byDay.Key.Date).AddDays(1),races)
                {Track=track});
        }

        var venues=tracks.Select(t=>
        {
            var events=history.Where(e=>e.Track?.Id==t.Value.Id).ToList();
            var ids=events.Select(e=>e.LocalId).ToHashSet();
            var classIds=events.SelectMany(e=>e.Races).Select(e=>e.ClassId).ToHashSet();
            var registrations=entries.Where(e=>e.Track==t.Key)
                .Select(e=>(e.Driver,e.Class)).ToHashSet();
            return new LiveTimeVenue(t.Value,ids,registrations,classIds);
        }).ToList();
        var rentals=rentalsTable.ActiveRows()
            .Select(r=>r.Text("Transponder")).Where(s=>s.Length>0&&s!="0")
            .Distinct().ToList();
        var roster=new LiveTimeRoster(325,rosterDrivers,rosterClasses,rosterEntries,
            history.Count,history.Sum(e=>e.RaceCount),
            history.Sum(e=>e.LapCount),0,rentals.Count){RentalNumbers=rentals};
        return new LiveTimeImportSource(roster,history){Venues=venues};
    }
}
