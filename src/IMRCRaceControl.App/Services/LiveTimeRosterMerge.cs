using IMRCRaceControl.Models;

namespace IMRCRaceControl.Services;

public sealed class LiveTimeRosterPlan
{
    public List<DriverProfile> Drivers { get; } = [];
    public List<ClassDefinition> Classes { get; } = [];
    public List<DriverClassAssignment> Assignments { get; } = [];
    public List<string> Issues { get; } = [];
    public int MatchedDrivers { get; internal set; }
    public int MatchedClasses { get; internal set; }
    public int MatchedAssignments { get; internal set; }
    public bool HasChanges => Drivers.Count+Classes.Count+Assignments.Count>0;
}

public static class LiveTimeRosterMerge
{
    private static readonly StringComparer IgnoreCase = StringComparer.OrdinalIgnoreCase;

    public static LiveTimeRosterPlan Preview(LiveTimeRoster source, IReadOnlyList<DriverProfile> drivers,
        IReadOnlyList<ClassDefinition> classes, IReadOnlyList<DriverClassAssignment> assignments,
        IEnumerable<string>? rentalNumbers = null)
    {
        var plan = new LiveTimeRosterPlan();
        var driverMap = new Dictionary<long,DriverProfile>();
        var classMap = new Dictionary<long,ClassDefinition>();
        var classNames = classes.ToLookup(c=>c.Name.Trim(),IgnoreCase);
        var sourceClassNames = source.Classes.ToLookup(c=>c.Name.Trim(),IgnoreCase);
        var sourceClassIds = source.Classes.ToLookup(c=>c.SourceId);
        var driverNames = drivers.ToLookup(d=>d.FullName.Trim(),IgnoreCase);
        var sourceDriverNames = source.Drivers.ToLookup(d=>FullName(d),IgnoreCase);
        var sourceDriverIds = source.Drivers.ToLookup(d=>d.SourceId);
        var driverTags = assignments.ToLookup(a=>a.DriverId,IgnoreCase);
        var sourceTags = source.Entries.ToLookup(e=>e.DriverId);
        var rentals = (rentalNumbers ?? []).Concat(source.RentalNumbers).Select(NormalizeTag).ToHashSet(IgnoreCase);

        foreach (var item in source.Classes)
        {
            var name = item.Name.Trim();
            if (name.Length == 0 || item.SourceId == Guid.Empty || sourceClassIds[item.SourceId].Count()!=1)
            { plan.Issues.Add("Skipped a class with a missing name or missing/duplicate source ID."); continue; }
            var id = "livetime-class-"+item.SourceId.ToString("N");
            var direct = classes.FirstOrDefault(c=>IgnoreCase.Equals(c.Id,id));
            var named = classNames[name].ToList();
            if (direct is null && (named.Count>1 || sourceClassNames[name].Count()>1))
            { plan.Issues.Add($"Skipped ambiguous class: {name}."); continue; }
            var match = direct ?? named.SingleOrDefault();
            if (match is null)
            {
                match = new ClassDefinition {Id=id,Name=name,PrintColor=PrintColor(item.Color)};
                if(item.MinimumLapSeconds is >=0 and <=3600)
                    match.MinimumLapSeconds=item.MinimumLapSeconds.Value;
                plan.Classes.Add(match);
            }
            else plan.MatchedClasses++;
            classMap.Add(item.LocalId,match);
        }

        foreach (var item in source.Drivers)
        {
            var name = FullName(item);
            if (name.Length == 0 || item.SourceId == Guid.Empty || sourceDriverIds[item.SourceId].Count()!=1)
            { plan.Issues.Add("Skipped a driver with a missing name or missing/duplicate source ID."); continue; }
            var id="livetime-driver-"+item.SourceId.ToString("N");
            var match=drivers.FirstOrDefault(d=>IgnoreCase.Equals(d.Id,id));
            var named=driverNames[name].ToList();
            if (match is null && named.Count>0)
            {
                var tags=sourceTags[item.LocalId].SelectMany(t=>Tags(t.Transponders))
                    .Where(t=>!rentals.Contains(t)).ToHashSet(IgnoreCase);
                var confirmed=named.Where(d=>driverTags[d.Id].SelectMany(a=>Tags(a.TransponderId)).Any(tags.Contains)).ToList();
                if(confirmed.Count!=1 || sourceDriverNames[name].Count()!=1)
                { plan.Issues.Add($"Skipped {name}: same name already exists and no unique transponder match."); continue; }
                match=confirmed[0];
            }
            if (match is null)
            {
                match=new DriverProfile {Id=id,FirstName=item.FirstName,LastName=item.LastName,Nickname=item.Nickname,PhoneticName=item.PhoneticName};
                match.SyncLegacyName();
                plan.Drivers.Add(match);
            }
            else plan.MatchedDrivers++;
            driverMap.Add(item.LocalId,match);
        }

        var existing = assignments.GroupBy(a=>(a.DriverId,a.ClassId))
            .ToDictionary(g=>g.Key,g=>g.First());
        var tagOwners = new Dictionary<string,HashSet<string>>(IgnoreCase);
        void RegisterTags(DriverClassAssignment assignment)
        {
            foreach(var tag in Tags(assignment.TransponderId))
            {
                if(!tagOwners.TryGetValue(tag,out var owners))tagOwners[tag]=owners=new(IgnoreCase);
                owners.Add(assignment.DriverId);
            }
        }
        foreach(var assignment in assignments)RegisterTags(assignment);
        var sourceTagOwners=source.Entries.SelectMany(e=>Tags(e.Transponders).Select(tag=>(tag,e.DriverId)))
            .ToLookup(x=>x.tag,x=>x.DriverId,IgnoreCase);
        foreach (var entry in source.Entries)
        {
            if (!driverMap.TryGetValue(entry.DriverId,out var driver) || !classMap.TryGetValue(entry.ClassId,out var cls)) continue;
            var pair=(driver.Id,cls.Id);
            var tags=Tags(entry.Transponders).Distinct(IgnoreCase).ToList();
            if(existing.TryGetValue(pair,out var current))
            {
                if(!tags.All(Tags(current.TransponderId).Contains))
                    plan.Issues.Add($"Kept existing assignment for {driver.FullName} in {cls.Name}.");
                else plan.MatchedAssignments++;
                continue;
            }
            var usable=new List<string>();
            foreach(var tag in tags)
            {
                if(rentals.Contains(tag) || sourceTagOwners[tag].Distinct().Skip(1).Any() ||
                    tagOwners.TryGetValue(tag,out var owners) && owners.Any(owner=>!IgnoreCase.Equals(owner,driver.Id)))
                { plan.Issues.Add($"Skipped shared or rental transponder {tag} for {driver.FullName} in {cls.Name}."); continue; }
                usable.Add(tag);
            }
            var addition = new DriverClassAssignment {DriverId=driver.Id,ClassId=cls.Id,TransponderId=string.Join(";",usable)};
            existing.Add(pair,addition);
            RegisterTags(addition);
            plan.Assignments.Add(addition);
        }
        return plan;
    }

    private static string FullName(LiveTimeDriver d) => $"{d.FirstName} {d.LastName}".Trim();

    private static IEnumerable<string> Tags(string value) => value.Split([';',',',' '],StringSplitOptions.RemoveEmptyEntries)
        .Select(NormalizeTag).Where(t=>t.Length>0);
    private static string NormalizeTag(string value) => value.Trim().TrimStart('0');

    private static string PrintColor(string value)
    {
        // LiveTime may include the alpha byte; Race Control uses an opaque palette.
        var color=value.TrimStart('#');
        if(color.Length==8)color=color[2..];
        if(color.Length!=6 || !int.TryParse(color,System.Globalization.NumberStyles.HexNumber,null,out var rgb))
            return ClassPrintColors.Choices[0].Hex;
        static int Distance(int a,int b)
        {
            var r=(a>>16)-(b>>16);var g=((a>>8)&255)-((b>>8)&255);var bl=(a&255)-(b&255);
            return r*r+g*g+bl*bl;
        }
        return ClassPrintColors.Choices.MinBy(c=>Distance(rgb,Convert.ToInt32(c.Hex[1..],16)))!.Hex;
    }
}
