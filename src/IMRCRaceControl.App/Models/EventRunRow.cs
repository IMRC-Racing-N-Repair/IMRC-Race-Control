namespace IMRCRaceControl.Models;

/// <summary>A display row for a run or one of its independently scored classes.</summary>
public sealed class EventRunRow
{
    private EventRunRow(EventSessionDefinition session, EventSessionDefinition lead,
        IReadOnlyList<EventSessionDefinition>? members, int memberOrder)
    {
        Session = session;
        Lead = lead;
        MemberOrder = memberOrder;
        RunMembers = members;
    }

    public EventSessionDefinition Session { get; }
    private EventSessionDefinition Lead { get; }
    private IReadOnlyList<EventSessionDefinition>? RunMembers { get; }
    private int MemberOrder { get; }
    public bool IsCombinedHeader => RunMembers is not null && MemberOrder == 0;
    public bool IsCombinedChild => MemberOrder > 0;
    public int RaceNumber => Lead.RaceNumber;
    public string RaceNumberDisplay => IsCombinedChild ? "" : RaceNumber.ToString();
    public string StatusDisplay => Session.StatusDisplay;
    public string TypeDisplay => Session.TypeDisplay;
    public string ClassDisplay => IsCombinedHeader ? "Combined Classes" :
        IsCombinedChild ? "    ↳ " + Session.ClassName : Session.ClassName;
    public string RoundLabel => Session.RoundLabel;
    public string HeatMainDisplay => IsCombinedHeader ? "—" : Session.HeatMainDisplay;
    public string MainRunDisplay => IsCombinedHeader ? "—" : Session.MainRunDisplay;
    public int RacerCount => IsCombinedHeader ? RunMembers!.Sum(s => s.RacerCount) : Session.RacerCount;
    public string Name => IsCombinedHeader ? $"One race • {RunMembers!.Count} class heats" : Session.Name;
    public string RoundGroupHeading => Session.RoundGroupHeading;
    public bool IsAlternateRoundBand => Session.IsAlternateRoundBand;
    public int RoundRaceCount { get; private set; }

    public static List<EventRunRow> Build(IEnumerable<EventSessionDefinition> sessions)
    {
        var all = sessions.ToList();
        var groups = all.Where(s => !string.IsNullOrEmpty(s.CombinedRaceId))
            .ToLookup(s => (s.CombinedRaceId, s.ScheduleStageOrder, s.ScheduleRoundOrder));
        var seen = new HashSet<(string, int, int)>();
        var rows = new List<EventRunRow>();
        foreach (var session in all)
        {
            var key = (session.CombinedRaceId, session.ScheduleStageOrder, session.ScheduleRoundOrder);
            var members = groups[key].OrderBy(s => s.CombinedRaceOrder).ThenBy(s => s.QueueOrder).ToList();
            if (members.Count < 2)
            {
                rows.Add(new(session, session, null, 0));
                continue;
            }
            if (!seen.Add(key)) continue;

            var lead = members[0];
            rows.Add(new(lead, lead, members, 0));
            for (var i = 0; i < members.Count; i++)
                rows.Add(new(members[i], lead, members, i + 1));
        }

        foreach (var round in rows.GroupBy(r => r.RoundGroupHeading))
        {
            var raceCount = round.Count(r => !r.IsCombinedChild);
            foreach (var row in round) row.RoundRaceCount = raceCount;
        }
        return rows;
    }

    public static int Compare(EventRunRow a, EventRunRow b, string property, bool descending)
    {
        var stage = a.Lead.ScheduleStageOrder.CompareTo(b.Lead.ScheduleStageOrder);
        if (stage != 0) return stage;
        var round = a.Lead.ScheduleRoundOrder.CompareTo(b.Lead.ScheduleRoundOrder);
        if (round != 0) return round;
        if (ReferenceEquals(a.Lead, b.Lead)) return a.MemberOrder.CompareTo(b.MemberOrder);
        var column = descending ? SortValue(b,property).CompareTo(SortValue(a,property))
            : SortValue(a,property).CompareTo(SortValue(b,property));
        if (column != 0) return column;
        var number = a.RaceNumber.CompareTo(b.RaceNumber);
        if (number != 0) return number;
        var queue = a.Lead.QueueOrder.CompareTo(b.Lead.QueueOrder);
        return queue != 0 ? queue : StringComparer.Ordinal.Compare(a.Lead.Id,b.Lead.Id);
    }

    private static IComparable SortValue(EventRunRow row, string property) => property switch
    {
        nameof(StatusDisplay) => row.Lead.StatusDisplay,
        nameof(TypeDisplay) => row.Lead.TypeDisplay,
        nameof(ClassDisplay) => row.RunMembers is null ? row.Lead.ClassName : "Combined Classes",
        nameof(HeatMainDisplay) => row.Lead.Heat,
        nameof(MainRunDisplay) => row.Lead.MainLeg,
        nameof(RacerCount) => row.RunMembers?.Sum(s => s.RacerCount) ?? row.Lead.RacerCount,
        nameof(Name) => row.RunMembers is null ? row.Lead.Name : $"One race • {row.RunMembers.Count} class heats",
        _ => row.Lead.RaceNumber
    };
}
