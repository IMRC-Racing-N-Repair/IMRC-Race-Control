using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

public sealed record DriverRatingResult(IReadOnlyList<DriverRatingRow> Standings, IReadOnlyList<DriverRatingRaceRow> History);

/// <summary>Rebuild from retained completed mains; corrections/restarts cannot apply the same rating twice.</summary>
public static class DriverRatingCalculator
{
    public static DriverRatingResult Calculate(IEnumerable<RaceDayEventDefinition> events,
        IEnumerable<DriverProfile> profiles, DriverRatingSettings settings, IEnumerable<DriverRatingReview>? reviews = null)
    {
        var drivers = profiles.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        var states = new Dictionary<(string Class, string Driver), DriverRatingRow>();
        var windows = new Dictionary<(string Class, string Driver), Queue<(double Quality, bool Incident)>>();
        var history = new List<DriverRatingRaceRow>();
        var reviewList = (reviews ?? []).ToList();
        var sessions = events.GroupBy(e => e.Id).Select(g => g.First())
            .SelectMany(e => e.Sessions.GroupBy(s => s.Id).Select(g => (Event: e, Session: g.First())))
            .Where(x => x.Session.SessionType == SessionType.Race && x.Session.ProgressStatus == EventSessionStatus.Completed)
            .OrderBy(x => x.Session.StartedAt ?? x.Session.CompletedAt ?? new DateTimeOffset(x.Event.EventDate))
            .ThenBy(x => x.Event.Id, StringComparer.Ordinal).ThenBy(x => x.Session.QueueOrder)
            .ThenBy(x => x.Session.Id, StringComparer.Ordinal);
        var windowSize = Math.Clamp(settings.SafetyWindowLaps, 50, 2000);
        var minLaps = Math.Clamp(settings.MinimumSafetyLaps, 3, 100);
        var slowdown = double.IsFinite(settings.IncidentSlowdownPercent) ? Math.Clamp(settings.IncidentSlowdownPercent, 5, 200) : 25;
        var seconds = double.IsFinite(settings.IncidentMinimumSeconds) ? Math.Clamp(settings.IncidentMinimumSeconds, .1, 60) : 1;
        var accidentPenalty = double.IsFinite(settings.AccidentPenalty) ? Math.Clamp(settings.AccidentPenalty, .05, 2) : .25;
        foreach (var (ev, session) in sessions)
        {
            // Identity comes from the saved roster. Never join names or transponder numbers.
            // Ambiguous duplicate car/driver identities are excluded instead of guessed.
            var entrants = session.Entrants.GroupBy(e => e.CarNumber).Where(g => g.Count() == 1).Select(g => g.Single())
                .Where(e => !string.IsNullOrWhiteSpace(e.DriverId) && !e.DriverId.StartsWith("unknown:", StringComparison.OrdinalIgnoreCase)).GroupBy(e => e.DriverId)
                .Where(g => g.Count() == 1).Select(g => g.Single()).ToDictionary(e => e.CarNumber);
            var starters = session.Results.GroupBy(r => r.CarNumber).Where(g => g.Count() == 1).Select(g => g.Single())
                .Where(r => r.Laps > 0 && r.Position > 0 && entrants.ContainsKey(r.CarNumber))
                .OrderBy(r => r.Position).ToList();
            if (starters.Count < 2) continue;
            foreach (var result in starters)
            {
                var entrant = entrants[result.CarNumber];
                var key = (session.ClassId, entrant.DriverId);
                if (states.ContainsKey(key)) continue;
                drivers.TryGetValue(entrant.DriverId, out var profile);
                states[key] = new DriverRatingRow
                {
                    DriverId = entrant.DriverId, Uuid = profile?.Uuid ?? DriverIdentity.UuidFor(entrant.DriverId),
                    DriverName = profile?.DisplayName ?? entrant.DisplayName,
                    ClassId = session.ClassId, ClassName = session.ClassName
                };
                windows[key] = new Queue<(double Quality, bool Incident)>();
            }
            var before = starters.ToDictionary(r => r.CarNumber, r => states[(session.ClassId, entrants[r.CarNumber].DriverId)].Rating);
            var sof = before.Values.Average();
            foreach (var result in starters)
            {
                var entrant = entrants[result.CarNumber];
                var key = (session.ClassId, entrant.DriverId);
                var row = states[key];
                var delta = 0d;
                foreach (var opponent in starters.Where(r => r.CarNumber != result.CarNumber))
                {
                    var actual = result.Position == opponent.Position ? .5 : result.Position < opponent.Position ? 1 : 0;
                    var expected = 1 / (1 + Math.Pow(10, (before[opponent.CarNumber] - before[result.CarNumber]) / 400));
                    delta += 32 * (actual - expected) / (starters.Count - 1);
                }
                row.Rating = Math.Clamp(before[result.CarNumber] + delta, 100, 4000);
                row.Races++;
                row.LastStrengthOfField = sof;
                var review = reviewList.LastOrDefault(r => r.EventId == ev.Id && r.SessionId == session.Id && r.DriverId == entrant.DriverId);
                // Lap one is excluded for grid/start effects. Use accepted corrected lap records only.
                var laps = session.Laps.Where(l => l.CarNumber == result.CarNumber && l.LapNumber > 1)
                    .OrderBy(l => l.LapNumber).ThenBy(l => l.Sequence)
                    .Select(l => l.LapTime.TotalSeconds).Where(t => double.IsFinite(t) && t > 0).ToList();
                var safetyBefore = row.Safety;
                var eligible = laps.Count >= minLaps && review?.ExcludeSafety != true;
                var incidents = 0;
                double? consistency = null;
                if (laps.Count > 1)
                {
                    var average = laps.Average();
                    var deviation = Math.Sqrt(laps.Average(t => (t - average) * (t - average)));
                    consistency = Math.Clamp(100 * (1 - deviation / average), 0, 100);
                }
                if (eligible)
                {
                    var sorted = laps.Order().ToList();
                    var median = sorted.Count % 2 == 0 ? (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2 : sorted[sorted.Count / 2];
                    var threshold = median + Math.Max(seconds, median * slowdown / 100);
                    foreach (var lap in laps)
                    {
                        var incident = lap > threshold;
                        if (incident) incidents++;
                        // Normalize to this driver's typical pace, so slower drivers are not penalized.
                        var quality = Math.Clamp(1 - Math.Abs(lap - median) / median, 0, 1);
                        windows[key].Enqueue((quality, incident));
                        while (windows[key].Count > windowSize) windows[key].Dequeue();
                    }
                }
                row.SafetyLaps = windows[key].Count;
                row.EstimatedIncidents = windows[key].Count(i => i.Incident);
                // 100-lap prior at 3.00; consistency builds the base, each estimated accident
                // subtracts its own explicit penalty within the rolling window.
                row.Safety = Math.Clamp((300 + 5 * windows[key].Sum(l => l.Quality)) / (row.SafetyLaps + 100)
                    - accidentPenalty * row.EstimatedIncidents, 0, 5);
                history.Add(new DriverRatingRaceRow
                {
                    EventId = ev.Id, SessionId = session.Id, DriverId = entrant.DriverId, ClassId = session.ClassId,
                    Date = session.StartedAt ?? session.CompletedAt ?? new DateTimeOffset(ev.EventDate),
                    EventName = ev.Name, SessionName = session.DashboardDisplay, DriverName = row.DriverName,
                    Position = result.Position, StrengthOfField = sof, RatingBefore = before[result.CarNumber], RatingAfter = row.Rating,
                    SafetyBefore = safetyBefore, SafetyAfter = row.Safety,
                    SafetyLaps = eligible ? laps.Count : 0, EstimatedIncidents = incidents, Consistency = consistency,
                    SafetyStatus = review?.ExcludeSafety == true ? "Excluded by review" : eligible ? "Estimated from lap consistency" : $"Needs {minLaps} laps after lap 1",
                    Note = review?.Note ?? ""
                });
            }
        }
        var standings = states.Values.OrderBy(r => r.ClassName).ThenByDescending(r => r.Rating).ThenBy(r => r.DriverName).ToList();
        foreach (var group in standings.GroupBy(r => r.ClassId))
        {
            var rank = 0;
            foreach (var row in group) row.Rank = ++rank;
        }
        return new(standings, history.OrderByDescending(r => r.Date).ToList());
    }
}
