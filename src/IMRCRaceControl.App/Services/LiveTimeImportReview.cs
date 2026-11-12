using IMRCRaceControl.Models;

namespace IMRCRaceControl.Services;

internal enum LiveTimeConflictAction { Merge, Copy, Discard }
internal sealed record LiveTimeMatch(string Id, string Label, bool CanMerge = true);
internal sealed record LiveTimeConflict(string Key, string Kind, string Incoming, string Detail,
    IReadOnlyList<LiveTimeMatch> Matches, bool CanCopy);
internal sealed record LiveTimeDecision(LiveTimeConflictAction Action, string? TargetId = null);

internal sealed class LiveTimeImportReview(
    IReadOnlyDictionary<string,LiveTimeDecision> decisions,
    IReadOnlyDictionary<string,string> previousLinks)
{
    public List<LiveTimeConflict> Pending { get; } = [];
    public Dictionary<string,string> Links { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string LinkedId(string key) => previousLinks.GetValueOrDefault(key) ?? key;

    public LiveTimeDecision Resolve(string key, string kind, string incoming, string detail, IReadOnlyList<LiveTimeMatch> matches, bool canCopy)
    {
        if (decisions.TryGetValue(key, out var decision) &&
            (decision.Action == LiveTimeConflictAction.Discard ||
             decision.Action == LiveTimeConflictAction.Copy && canCopy ||
             decision.Action == LiveTimeConflictAction.Merge && matches.Any(m => m.CanMerge && m.Id == decision.TargetId)))
            return decision;
        Pending.Add(new(key, kind, incoming, detail, matches, canCopy));
        return new(LiveTimeConflictAction.Discard);
    }

    public void Remember(string key, string id)
    {
        if (LinkedId(key) != id) Links[key] = id;
    }

    public static string CopyId(string id, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(id)) return id;
        for (var i = 2; ; i++)
        {
            var copy = $"{id}-copy-{i}";
            if (!used.Contains(copy)) return copy;
        }
    }

    public static string CopyName(string name, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var copy = $"{name} (imported copy)";
        for (var i = 2; used.Contains(copy); i++) copy = $"{name} (imported copy {i})";
        return copy;
    }

    public static string NameKey(string name) =>
        string.Join(" ", name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

internal sealed record LiveTimeDriverUpdate(DriverProfile Driver, string Nickname, string PhoneticName)
{
    public void Apply() { Driver.Nickname = Nickname; Driver.PhoneticName = PhoneticName; }
    public static LiveTimeDriverUpdate Snapshot(DriverProfile driver) => new(driver, driver.Nickname, driver.PhoneticName);
}

internal sealed record LiveTimeEventUpdate(RaceDayEventDefinition Event, TrackProfile? Track,
    IReadOnlyList<EventSessionDefinition> Sessions);
internal sealed record LiveTimeHistoryReview(List<RaceDayEventDefinition> Events, List<LiveTimeEventUpdate> Updates);

internal static class LiveTimeReviewedHistory
{
    private static readonly StringComparer IgnoreCase = StringComparer.OrdinalIgnoreCase;
    private static bool SameName(string a, string b) =>
        IgnoreCase.Equals(LiveTimeImportReview.NameKey(a),LiveTimeImportReview.NameKey(b));

    internal static (List<TrackProfile> NewTracks, Dictionary<string,TrackProfile?> TrackMap) Tracks(
        LiveTimeImportSource source, IReadOnlySet<string> selected, IEnumerable<TrackProfile> existing, LiveTimeImportReview review)
    {
        var known = existing.DistinctBy(t => t.Id, IgnoreCase).ToList();
        var added = new List<TrackProfile>();
        var map = new Dictionary<string,TrackProfile?>(IgnoreCase);
        foreach (var track in source.Venues.Where(v => selected.Contains(v.Id)).Select(v => v.Track).OfType<TrackProfile>())
        {
            var direct = known.FirstOrDefault(t => IgnoreCase.Equals(t.Id,review.LinkedId(track.Id)));
            var matches = direct is not null ? [direct] : known.Where(t => SameName(t.Name,track.Name)).ToList();
            if (matches.Count == 0)
            {
                var next = track.Snapshot();
                added.Add(next);
                known.Add(next);
                map[track.Id] = next;
                continue;
            }
            var decision = review.Resolve(track.Id,"Track",track.Name,"Merge uses the saved track and keeps its settings and records.",
                matches.Select(t => new LiveTimeMatch(t.Id,t.Name + $" • ID {t.Id[^Math.Min(t.Id.Length,8)..]}")).ToList(),true);
            if (decision.Action == LiveTimeConflictAction.Copy)
            {
                var copy = track.Snapshot();
                copy.Id = LiveTimeImportReview.CopyId(track.Id,known.Select(t => t.Id));
                copy.Name = LiveTimeImportReview.CopyName(track.Name,known.Select(t => t.Name));
                added.Add(copy);
                known.Add(copy);
                map[track.Id] = copy;
            }
            else if (decision.Action == LiveTimeConflictAction.Merge)
            {
                var target = matches.Single(t => t.Id == decision.TargetId);
                review.Remember(track.Id,target.Id);
                map[track.Id] = target;
            }
            else map[track.Id] = matches.Count == 1 ? matches[0] : null;
        }
        return (added,map);
    }

    internal static LiveTimeHistoryReview Events(LiveTimeImportSource source, IReadOnlyCollection<Guid> chosen,
        IReadOnlyList<RaceDayEventDefinition> existing, LiveTimeRosterPlan roster, IReadOnlyDictionary<string,TrackProfile?> tracks,
        TrackProfile savedTrack, LiveTimeImportReview review)
    {
        var result = new LiveTimeHistoryReview([],[]);
        if (chosen.Count == 0) return result;
        var known = existing.ToList();
        var allRaceIds = known.SelectMany(e => e.Sessions.Select(s => (s.Id, Event: e)))
            .GroupBy(r => r.Id,IgnoreCase).ToDictionary(g => g.Key,g => g.First().Event,IgnoreCase);
        var incomingEvents = LiveTimeHistoryMerge.Preview(source,chosen,[],roster,includeLaps:true,savedTrack:savedTrack,trackMap:tracks);
        foreach (var incoming in incomingEvents)
        {
            var sourceId = incoming.Id;
            var direct = known.FirstOrDefault(e => IgnoreCase.Equals(e.Id,review.LinkedId(sourceId)));
            var sameRace = incoming.Sessions.Select(s => allRaceIds.GetValueOrDefault(s.Id)).OfType<RaceDayEventDefinition>().Distinct().ToList();
            var matches = direct is not null ? [direct] : sameRace.Count > 0 ? sameRace : known.Where(e =>
                e.EventDate.Date == incoming.EventDate.Date && SameName(e.Name,incoming.Name) &&
                (incoming.Track is null || e.Track is null || IgnoreCase.Equals(incoming.Track.Id,e.Track.Id))).ToList();
            if (matches.Count > 0)
            {
                var decision = review.Resolve(sourceId,"Event",$"{incoming.Name} • {incoming.EventDate:d} • {incoming.Track?.Name ?? "No track"}",
                    "Merge adds missing races to a completed event and keeps saved results. An active event cannot be merged.",
                    matches.Select(e => new LiveTimeMatch(e.Id,$"{e.Name} • {e.EventDate:d} • {e.Track?.Name ?? "No track"} • {e.StatusDisplay}",
                        e.Status == EventStatus.Completed)).ToList(),true);
                if (decision.Action == LiveTimeConflictAction.Discard) continue;
                if (decision.Action == LiveTimeConflictAction.Merge)
                {
                    var target = matches.Single(e => e.Id == decision.TargetId);
                    review.Remember(sourceId,target.Id);
                    var prior = result.Updates.FirstOrDefault(x => x.Event == target);
                    var planned = prior?.Sessions.ToList() ?? [];
                    var queue = target.Sessions.Concat(planned).Select(s => s.QueueOrder).DefaultIfEmpty(0).Max();
                    var available = target.Sessions.Concat(planned).ToList();
                    foreach (var race in incoming.Sessions)
                    {
                        if (allRaceIds.ContainsKey(race.Id) || available.Any(s => SameRaceSlot(s,race))) continue;
                        race.QueueOrder = ++queue;
                        planned.Add(race);
                        available.Add(race);
                        allRaceIds[race.Id] = target;
                    }
                    var track = target.Track ?? prior?.Track ?? incoming.Track;
                    if (prior is not null) result.Updates.Remove(prior);
                    if (planned.Count > 0 || target.Track is null && track is not null)
                        result.Updates.Add(new(target,track,planned));
                    continue;
                }

                incoming.Id = LiveTimeImportReview.CopyId(sourceId,known.Select(e => e.Id));
                incoming.Name = LiveTimeImportReview.CopyName(incoming.Name,known.Select(e => e.Name));
                foreach (var race in incoming.Sessions) race.Id = LiveTimeImportReview.CopyId(race.Id,allRaceIds.Keys);
            }
            result.Events.Add(incoming);
            known.Add(incoming);
            foreach (var race in incoming.Sessions) allRaceIds.TryAdd(race.Id,incoming);
        }
        return result;
    }

    private static bool SameRaceSlot(EventSessionDefinition a, EventSessionDefinition b) =>
        (IgnoreCase.Equals(a.ClassId,b.ClassId) || SameName(a.ClassName,b.ClassName)) &&
        a.SessionType == b.SessionType && a.Heat == b.Heat &&
        (a.SessionType == SessionType.Race ? a.MainLeg == b.MainLeg : a.Round == b.Round);
}
