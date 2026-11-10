using IMRCRaceControl.Models;

namespace IMRCRaceControl.Services;

[Flags]
public enum LiveTimeRosterSelection
{
    None = 0,
    Drivers = 1,
    Classes = 2,
    ClassAssignments = 4,
    Transponders = 8,
    ClassSettings = 16,
    All = Drivers | Classes | ClassAssignments | Transponders
}

internal sealed record LiveTimeClassUpdate(ClassDefinition Class, LiveTimeClassValues Before, LiveTimeClassValues After);

internal sealed record LiveTimeClassValues(double Minimum, RaceFormat QualFormat, StartStyle QualStart, double QualMinutes,
    int QualLaps, int HeatCapacity, RaceFormat? MainFormat, StartStyle MainStart, double? MainMinutes,
    int? MainLaps, int MainCapacity, List<MainRuleDefinition> MainRules)
{
    internal static LiveTimeClassValues Capture(ClassDefinition c) => new(c.MinimumLapSeconds, c.RaceFormat,
        c.QualStartStyle, c.RaceMinutes, c.TargetLaps, c.MaxRacersPerHeat, c.MainRaceFormat, c.RaceStartStyle,
        c.MainRaceMinutes, c.MainTargetLaps, c.MaxRacersPerMain, c.MainRules);

    internal LiveTimeClassValues WithSource(LiveTimeClass source)
    {
        var t = source.Timing;
        var mainCapacity = Capacity(t?.MainCapacity) ?? MainCapacity;
        var rules = MainRules;
        // Keep custom per-main limits and advancement rules intact.
        if (mainCapacity != MainCapacity && rules is [{ Order: 1 } a] && a.MaxRacers == MainCapacity
            && a.BumpInsFromLower < mainCapacity)
        {
            var updated = a.Clone();
            updated.MaxRacers = mainCapacity;
            rules = [updated];
        }
        return this with
        {
            Minimum = MinimumLap(source.MinimumLapSeconds) ?? MinimumLap(t?.MinimumLap) ?? Minimum,
            QualFormat = Format(t?.QualFormat) ?? QualFormat,
            QualStart = Start(t?.QualStart) ?? QualStart,
            QualMinutes = Minutes(t?.QualSeconds) ?? QualMinutes,
            QualLaps = Laps(t?.QualLaps) ?? QualLaps,
            HeatCapacity = Capacity(t?.HeatCapacity) ?? HeatCapacity,
            MainFormat = Format(t?.MainFormat) ?? MainFormat,
            MainStart = Start(t?.MainStart) ?? MainStart,
            MainMinutes = Minutes(t?.MainSeconds) ?? MainMinutes,
            MainLaps = Laps(t?.MainLaps) ?? MainLaps,
            MainCapacity = mainCapacity,
            MainRules = rules
        };
    }

    internal void ApplyTo(ClassDefinition c)
    {
        c.MinimumLapSeconds=Minimum; c.RaceFormat=QualFormat; c.QualStartStyle=QualStart;
        c.RaceMinutes=QualMinutes; c.TargetLaps=QualLaps; c.MaxRacersPerHeat=HeatCapacity;
        c.MainRaceFormat=MainFormat; c.RaceStartStyle=MainStart; c.MainRaceMinutes=MainMinutes;
        c.MainTargetLaps=MainLaps; c.MaxRacersPerMain=MainCapacity; c.MainRules=MainRules;
    }

    private static double? MinimumLap(double? value) => value is >= 0 and <= 3600 ? value : null;
    private static int? Capacity(int? value) => value is >= 1 and <= 30 ? value : null;
    private static int? Laps(int? value) => value is >= 1 and <= 10000 ? value : null;
    private static double? Minutes(int? seconds) => seconds is >= 1 and <= 86400 ? seconds / 60.0 : null;
    private static RaceFormat? Format(int? id) => id switch { 1 => RaceFormat.Timed, 2 => RaceFormat.Laps, _ => null };
    private static StartStyle? Start(int? id) => id switch { 1 => StartStyle.IndividualCrossing, 2 => StartStyle.Tone, _ => null };
}

public sealed record LiveTimeTagFill(string AssignmentId, string Transponders);

public sealed class LiveTimeRosterPlan
{
    public List<DriverProfile> Drivers { get; } = [];
    public List<ClassDefinition> Classes { get; } = [];
    public List<DriverClassAssignment> Assignments { get; } = [];
    internal Dictionary<long,string> DriverIds { get; } = [];
    internal Dictionary<long,string> ClassIds { get; } = [];
    public List<LiveTimeTagFill> TagFills { get; } = [];
    internal List<LiveTimeClassUpdate> ClassUpdates { get; } = [];
    public List<string> Issues { get; } = [];
    public int MatchedDrivers { get; internal set; }
    public int MatchedClasses { get; internal set; }
    public int MatchedAssignments { get; internal set; }
    public int SkippedMissingAssignments { get; internal set; }
    public bool HasChanges => Drivers.Count + Classes.Count + Assignments.Count + TagFills.Count + ClassUpdates.Count > 0;
}

public static class LiveTimeRosterMerge
{
    private static readonly StringComparer IgnoreCase = StringComparer.OrdinalIgnoreCase;

    public static int CountPersonalTransponders(LiveTimeRoster source)
    {
        var rentals = source.RentalNumbers.Select(NormalizeTag).ToHashSet(IgnoreCase);
        return source.Entries.SelectMany(entry => Tags(entry.Transponders))
            .Where(tag => !rentals.Contains(tag))
            .Distinct(IgnoreCase)
            .Count();
    }

    public static LiveTimeRosterPlan Preview(LiveTimeRoster source, IReadOnlyList<DriverProfile> drivers,
        IReadOnlyList<ClassDefinition> classes, IReadOnlyList<DriverClassAssignment> assignments,
        IEnumerable<string>? rentalNumbers = null, LiveTimeRosterSelection selection = LiveTimeRosterSelection.All, bool resolveForHistory = false)
    {
        var plan = new LiveTimeRosterPlan();
        if (selection == LiveTimeRosterSelection.None && !resolveForHistory) return plan;
        var includeDrivers = selection.HasFlag(LiveTimeRosterSelection.Drivers);
        var includeClasses = selection.HasFlag(LiveTimeRosterSelection.Classes);
        var includeAssignments = selection.HasFlag(LiveTimeRosterSelection.ClassAssignments);
        var includeTags = selection.HasFlag(LiveTimeRosterSelection.Transponders);
        var includeSettings = selection.HasFlag(LiveTimeRosterSelection.ClassSettings);
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

        foreach (var item in source.Classes.Where(_ => includeClasses || includeAssignments || includeTags || includeSettings || resolveForHistory))
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
                if (!includeClasses) continue;
                match = new ClassDefinition {Id=id,Name=name,PrintColor=PrintColor(item.Color)};
                if(item.MinimumLapSeconds is >=0 and <=3600)
                    match.MinimumLapSeconds=item.MinimumLapSeconds.Value;
                if (includeSettings)
                    LiveTimeClassValues.Capture(match).WithSource(item).ApplyTo(match);
                plan.Classes.Add(match);
            }
            else
            {
                plan.MatchedClasses++;
                if (includeSettings)
                {
                    var before = LiveTimeClassValues.Capture(match);
                    var after = before.WithSource(item);
                    if (after != before)
                        plan.ClassUpdates.Add(new(match, before, after));
                }
            }
            classMap.Add(item.LocalId,match);
            plan.ClassIds[item.LocalId] = match.Id;
        }

        foreach (var item in source.Drivers.Where(_ => includeDrivers || includeAssignments || includeTags || resolveForHistory))
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
                if (!includeDrivers) continue;
                match=new DriverProfile {Id=id,FirstName=item.FirstName,LastName=item.LastName,Nickname=item.Nickname,PhoneticName=item.PhoneticName};
                match.SyncLegacyName();
                plan.Drivers.Add(match);
            }
            else plan.MatchedDrivers++;
            driverMap.Add(item.LocalId,match);
            plan.DriverIds[item.LocalId] = match.Id;
        }

        if (!includeAssignments && !includeTags) return plan;

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
            if (!driverMap.TryGetValue(entry.DriverId,out var driver) || !classMap.TryGetValue(entry.ClassId,out var cls))
            {
                plan.SkippedMissingAssignments++;
                continue;
            }
            var pair=(driver.Id,cls.Id);
            var tags = includeTags ? Tags(entry.Transponders).Distinct(IgnoreCase).ToList() : [];
            var hasAssignment = existing.TryGetValue(pair, out var current);
            if (!hasAssignment && !includeAssignments)
            {
                if (tags.Count > 0) plan.SkippedMissingAssignments++;
                continue;
            }

            if (current is not null)
            {
                var existingTags = Tags(current.TransponderId).ToHashSet(IgnoreCase);
                if (tags.All(existingTags.Contains))
                {
                    plan.MatchedAssignments++;
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(current.TransponderId))
                {
                    plan.Issues.Add($"Kept existing transponders for {driver.FullName} in {cls.Name}.");
                    continue;
                }
            }

            var usable=new List<string>();
            foreach(var tag in tags)
            {
                if(rentals.Contains(tag) || sourceTagOwners[tag].Distinct().Skip(1).Any() ||
                    tagOwners.TryGetValue(tag,out var owners) && owners.Any(owner=>!IgnoreCase.Equals(owner,driver.Id)))
                { plan.Issues.Add($"Skipped shared or rental transponder {tag} for {driver.FullName} in {cls.Name}."); continue; }
                usable.Add(tag);
            }
            var transponders = string.Join(";", usable);
            if (current is not null)
            {
                if (usable.Count == 0) continue;
                plan.TagFills.Add(new(current.Id, transponders));
                RegisterTags(new DriverClassAssignment { DriverId = driver.Id, TransponderId = transponders });
                continue;
            }
            var addition = new DriverClassAssignment {DriverId=driver.Id,ClassId=cls.Id,TransponderId=transponders};
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
