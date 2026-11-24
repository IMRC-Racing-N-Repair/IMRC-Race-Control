using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

/// <summary>Bounded, deterministic search. Never changes a race, roster or saved result.</summary>
public static class RaceOrderAssistant
{
    public sealed record Metrics(int Conflicts, int AffectedDrivers, int RestScore, int MovedRaces);
    public sealed record Suggestion(string Name, IReadOnlyList<EventSessionDefinition> Order, Metrics Score, DashboardRoundChoice Round);
    private sealed record Candidate(int[] Order, Metrics Score);
    private sealed record RaceUnit(EventSessionDefinition[] Sessions, HashSet<string> Drivers)
    {
        public EventSessionDefinition First => Sessions[0];
        // Keep combined races and loaded/running/completed races fixed. Planned
        // individual races can move only within their original qualifying round/main leg.
        public bool Movable => Sessions.Length == 1 && string.IsNullOrEmpty(First.CombinedRaceId) &&
            First.ProgressStatus == EventSessionStatus.Planned &&
            First.SessionType is SessionType.Qualifying or SessionType.Race;
        public string Group => $"{First.SessionType}:{First.ScheduleRoundOrder}";
    }

    public static IReadOnlyList<Suggestion> Find(IReadOnlyList<EventSessionDefinition> schedule, DashboardRoundChoice? round = null)
    {
        // A suggestion can change exactly one round. Keep adjacent rounds in scoring context only.
        round ??= schedule.Where(s=>s.SessionType is SessionType.Qualifying or SessionType.Race)
            .Select(DashboardRoundChoice.From).FirstOrDefault();
        if(round is null)return [];
        var races = schedule.Where(s => s.ProgressStatus != EventSessionStatus.Skipped)
            .GroupBy(s => string.IsNullOrEmpty(s.CombinedRaceId) ? "session:" + s.Id : "combined:" + s.CombinedRaceId,
                StringComparer.OrdinalIgnoreCase)
            .Select(g => new RaceUnit(g.ToArray(),g.SelectMany(s=>s.Entrants)
                .Where(e=>!string.IsNullOrWhiteSpace(e.DriverId)).Select(e=>e.DriverId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase))).ToArray();
        var initial = Enumerable.Range(0,races.Length).ToArray();
        var baseline = Evaluate(initial);
        if (baseline.Conflicts == 0) return [];
        var blocks = new List<(int Start,int End)>();
        for(var i=0;i<races.Length;)
        {
            if(!races[i].Movable || !round.Matches(races[i].First)){i++;continue;}
            var start=i++;
            while(i<races.Length && races[i].Movable && races[i].Group==races[start].Group)i++;
            if(i-start>1)blocks.Add((start,i));
        }
        if(blocks.Count==0)return [];
        // Retain each class's existing relative heat/main order and explicit bump-up dependencies.
        var classEdges = races.SelectMany((r,i)=>r.Sessions.Select(s=>(s,i)))
            .GroupBy(x=>(x.s.SessionType,x.s.ClassId.ToUpperInvariant(),x.s.SessionType==SessionType.Race?0:x.s.Round))
            .SelectMany(g=>g.Select(x=>x.i).Distinct().Zip(g.Select(x=>x.i).Distinct().Skip(1)))
            .ToList();
        var ids=races.SelectMany((r,i)=>r.Sessions.Select(s=>(s.Id,i))).ToDictionary(x=>x.Id,x=>x.i,StringComparer.OrdinalIgnoreCase);
        foreach(var (race,index) in races.Select((r,i)=>(r,i)))
            foreach(var session in race.Sessions)
                if(!string.IsNullOrEmpty(session.BumpTargetSessionId) && ids.TryGetValue(session.BumpTargetSessionId,out var target)&&target!=index)
                    classEdges.Add((index,target));
        bool Valid(int[] order)
        {
            var positions=new int[order.Length];
            for(var i=0;i<order.Length;i++)positions[order[i]]=i;
            return classEdges.All(e=>positions[e.First]<positions[e.Second]);
        }
        Metrics Evaluate(int[] order)
        {
            var last=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            var affected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int conflicts=0,rest=0,moved=0;
            for(var i=0;i<order.Length;i++)
            {
                if(order[i]!=i)moved++;
                foreach(var driver in races[order[i]].Drivers)
                {
                    if(last.TryGetValue(driver,out var prior))
                    {
                        var historic=races[order[prior]].Sessions.All(s=>s.ProgressStatus==EventSessionStatus.Completed)
                            && races[order[i]].Sessions.All(s=>s.ProgressStatus==EventSessionStatus.Completed);
                        if(!historic)
                        {
                            var gap=i-prior-1;
                            if(gap==0){conflicts++;affected.Add(driver);}
                            rest+=Math.Min(5,gap);
                        }
                    }
                    last[driver]=i;
                }
            }
            return new(conflicts,affected.Count,rest,moved);
        }
        var pool=new List<Candidate>();
        var seen=new HashSet<string>(StringComparer.Ordinal){string.Join(",",initial)};
        var frontier=new List<Candidate>{new(initial,baseline)};
        int attempts=0;
        const int budget=5000;
        // Keep diverse candidates for all three priorities. Search is intentionally
        // bounded for large race days; never claim a mathematical optimum.
        for(var depth=0;depth<5 && frontier.Count>0 && attempts<budget;depth++)
        {
            var next=new List<Candidate>();
            foreach(var state in frontier)
            {
                foreach(var block in blocks)
                {
                    for(var from=block.Start;from<block.End && attempts<budget;from++)
                    for(var to=block.Start;to<block.End && attempts<budget;to++)
                    {
                        if(from==to)continue;
                        attempts++;
                        var order=(int[])state.Order.Clone();
                        var moving=order[from];
                        if(from<to)Array.Copy(order,from+1,order,from,to-from);
                        else Array.Copy(order,to,order,to+1,from-to);
                        order[to]=moving;
                        if(!seen.Add(string.Join(",",order)) || !Valid(order))continue;
                        var score=Evaluate(order);
                        var candidate=new Candidate(order,score);
                        next.Add(candidate);
                        if(score.Conflicts<baseline.Conflicts)pool.Add(candidate);
                    }
                    if(attempts>=budget)break;
                }
                if(attempts>=budget)break;
            }
            frontier=next.OrderBy(c=>c.Score.Conflicts).ThenBy(c=>c.Score.MovedRaces).Take(12)
                .Concat(next.OrderByDescending(c=>c.Score.RestScore).ThenBy(c=>c.Score.Conflicts).Take(6))
                .Distinct().ToList();
        }
        if(pool.Count==0)return [];
        var least=pool.OrderBy(c=>c.Score.MovedRaces).ThenBy(c=>c.Score.Conflicts).ThenByDescending(c=>c.Score.RestScore).First();
        var fewest=pool.OrderBy(c=>c.Score.Conflicts).ThenBy(c=>c.Score.AffectedDrivers).ThenBy(c=>c.Score.MovedRaces).ThenByDescending(c=>c.Score.RestScore).First();
        var restBest=pool.OrderByDescending(c=>c.Score.RestScore).ThenBy(c=>c.Score.Conflicts).ThenBy(c=>c.Score.MovedRaces).First();
        return new[]{Make("Least Changes",least),Make("Fewest Conflicts",fewest),Make("Maximum Rest",restBest)};
        Suggestion Make(string name,Candidate c)=>new(name,c.Order.SelectMany(i=>races[i].Sessions).ToList(),c.Score,round);
    }
}
