using System.Globalization;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

public static class SeriesScoring
{
    public static bool TrySchedule(string text, out List<decimal> points)
    {
        points = [];
        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries))
        {
            if (!decimal.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var p) || p < 0 || p > 1000000)
                return false;
            points.Add(p);
        }
        return points.Count > 0;
    }

    public static List<RaceDayEventDefinition> SelectedEvents(RaceSeriesDefinition series, IEnumerable<RaceDayEventDefinition> events) => events
        .Where(e => series.EventIds.Contains(e.Id) && e.EventDate.Date >= series.StartDate.Date && e.EventDate.Date <= series.EndDate.Date)
        .OrderBy(e => e.EventDate).ThenBy(e => e.Id).ToList();

    public static List<(string DriverId, string DriverName)> MainOrder(RaceDayEventDefinition ev, string classId)
    {
        var results = new List<(string DriverId, string DriverName)>();
        var seen = new HashSet<string>();
        foreach (var group in ev.Sessions.Where(s => s.ClassId == classId && s.SessionType == SessionType.Race).GroupBy(s => s.Heat).OrderBy(g => g.Key))
        {
            if (group.Any(s => s.ProgressStatus is not EventSessionStatus.Completed and not EventSessionStatus.Skipped)) continue;
            foreach (var row in MainResultCalculator.CalculateMainSeriesStandings(group.ToList()))
                if (seen.Add(row.Entrant.DriverId)) results.Add((row.Entrant.DriverId, row.Entrant.DisplayName));
        }
        return results;
    }

    public static List<(string DriverId, string DriverName)> QualifyingOrder(RaceDayEventDefinition ev, string classId, int? round = null, bool useEventRanking = true)
    {
        var sessions = ev.Sessions.Where(s => s.ClassId == classId && s.SessionType == SessionType.Qualifying &&
            s.ProgressStatus == EventSessionStatus.Completed && (!round.HasValue || s.Round == round)).ToList();
        var performances = sessions.SelectMany(s => s.Entrants.Select(e => new {
            e.DriverId, Name = e.DisplayName,
            Laps = s.Results.FirstOrDefault(r => r.CarNumber == e.CarNumber)?.Laps ?? s.Laps.Count(l => l.CarNumber == e.CarNumber),
            Elapsed = s.Laps.Where(l => l.CarNumber == e.CarNumber).Select(l => l.Elapsed.TotalSeconds).DefaultIfEmpty(double.MaxValue).Max()
        })).Where(p => p.Laps > 0).GroupBy(p => p.DriverId)
          .Select(g => g.OrderByDescending(p => p.Laps).ThenBy(p => p.Elapsed).First()).ToList();
        // Use the event's established overall qualifying ranking when available.
        if (useEventRanking && !round.HasValue && ev.Rankings.Any(r => r.ClassId == classId))
        {
            var ranks = ev.Rankings.Where(r => r.ClassId == classId).ToDictionary(r => r.DriverId, r => r.Rank);
            return performances.OrderBy(p => ranks.GetValueOrDefault(p.DriverId, int.MaxValue))
                .ThenByDescending(p => p.Laps).ThenBy(p => p.Elapsed).ThenBy(p => p.Name)
                .Select(p => (p.DriverId, p.Name)).ToList();
        }
        return performances.OrderByDescending(p => p.Laps).ThenBy(p => p.Elapsed).ThenBy(p => p.Name)
            .Select(p => (p.DriverId, p.Name)).ToList();
    }

    private static int LapCount(IEnumerable<EventSessionDefinition> sessions, string id) => sessions.Sum(s => {
        var entrant = s.Entrants.FirstOrDefault(e => e.DriverId == id);
        return entrant is null ? 0 : s.Results.FirstOrDefault(r => r.CarNumber == entrant.CarNumber)?.Laps
            ?? s.Laps.Count(l => l.CarNumber == entrant.CarNumber);
    });

    private static decimal PositionAward(StagePointsSettings rules, int position)
    {
        if (position <= 0) return 0;
        TrySchedule(rules.PositionPoints, out var schedule);
        return (position <= schedule.Count ? schedule[position - 1] : rules.UnlistedPositionPoints)
            + (position == 1 ? rules.WinBonus : 0);
    }

    private static bool Fastest(IEnumerable<EventSessionDefinition> sessions, string id)
    {
        var laps = sessions.SelectMany(s => s.Laps.Where(l => l.LapTime > TimeSpan.Zero)
            .Select(l => (DriverId: s.Entrants.FirstOrDefault(e => e.CarNumber == l.CarNumber)?.DriverId, l.LapTime))).ToList();
        return laps.Count > 0 && laps.Any(l => l.DriverId == id && l.LapTime == laps.Min(x => x.LapTime));
    }

    public static List<SeriesStandingRow> Calculate(RaceSeriesDefinition series, IEnumerable<RaceDayEventDefinition> events, string classId, IEnumerable<DriverProfile>? drivers = null)
    {
        var selected = SelectedEvents(series, events);
        var rules = series.EffectivePoints;
        // The event's class signup is authoritative. A generated/stale lineup,
        // global driver directory entry or points adjustment is not a signup.
        // Keep actual racers even if their registration was later removed.
        var classSessions = selected.SelectMany(ev => ev.Sessions)
            .Where(s => s.ClassId == classId && s.SessionType is SessionType.Qualifying or SessionType.Race).ToList();
        var eligibleIds = selected.SelectMany(ev => ev.DriverAssignments)
            .Where(a => a.ClassId == classId).Select(a => a.DriverId)
            .Concat(classSessions.SelectMany(s => s.Entrants.Where(e =>
                s.Results.Any(result => result.CarNumber == e.CarNumber && result.Laps > 0) ||
                s.Laps.Any(lap => lap.CarNumber == e.CarNumber)).Select(e => e.DriverId)))
            .Concat(selected.SelectMany(ev => ev.Rankings).Where(rank => rank.ClassId == classId && rank.TotalLaps > 0).Select(rank => rank.DriverId))
            .Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Directory and adjustment data can resolve names, but never add rows.
        var names = classSessions.SelectMany(s => s.Entrants).Select(e => (Id: e.DriverId, Name: e.DisplayName))
            .Concat((drivers ?? []).Select(d => (Id: d.Id, Name: d.DisplayName)))
            .Concat(selected.SelectMany(ev => ev.Rankings).Where(rank => rank.ClassId == classId).Select(rank => (Id: rank.DriverId, Name: rank.DriverName)))
            .Concat(series.Adjustments.Where(a => a.ClassId == classId).Select(a => (Id: a.DriverId, Name: a.DriverName)))
            .Where(d => !string.IsNullOrWhiteSpace(d.Id) && !string.IsNullOrWhiteSpace(d.Name))
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
        var candidates = eligibleIds.Select(id => (Id: id, Name: names.GetValueOrDefault(id, "Unknown driver"))).ToList();
        var rows = new List<SeriesStandingRow>();
        foreach (var driver in candidates)
        {
            var row = new SeriesStandingRow { DriverId = driver.Id, DriverName = driver.Name };
            foreach (var ev in selected)
            {
                var week = new SeriesWeekScore { EventId = ev.Id, EventName = ev.Name, EventDate = ev.EventDate, Pending = ev.Status != EventStatus.Completed };
                row.Weeks.Add(week);
                if (week.Pending) continue;
                var sessions = ev.Sessions.Where(s => s.ClassId == classId && s.ProgressStatus == EventSessionStatus.Completed).ToList();
                var quals = sessions.Where(s => s.SessionType == SessionType.Qualifying).ToList();
                var mains = sessions.Where(s => s.SessionType == SessionType.Race).ToList();
                var qOrder = QualifyingOrder(ev, classId);
                // TQ is the best individual qualifying run across every heat/round,
                // independent of points modes or the event's combined ranking.
                var tqOrder = QualifyingOrder(ev, classId, useEventRanking: false);
                var isTopQualifier = tqOrder.Count > 0 && tqOrder[0].DriverId == driver.Id;
                var mOrder = MainOrder(ev, classId);
                var qLaps = LapCount(quals, driver.Id);
                var mLaps = LapCount(mains, driver.Id);
                week.Attended = qLaps + mLaps > 0;
                var qPosition = qOrder.FindIndex(d => d.DriverId == driver.Id) + 1;
                var mPosition = mOrder.FindIndex(d => d.DriverId == driver.Id) + 1;
                if (rules.LegacyQualifyingFallback && mOrder.Count == 0) mPosition = qPosition;
                week.Finish = mPosition > 0 ? mPosition : qPosition;
                if (rules.Qualifying.Enabled && qLaps >= rules.Qualifying.MinimumLaps && qLaps > 0)
                {
                    var awards = quals.Select(s => s.Round).Distinct().Select(round =>
                        PositionAward(rules.Qualifying, QualifyingOrder(ev, classId, round).FindIndex(d => d.DriverId == driver.Id) + 1)).ToList();
                    week.Qualifying = rules.Qualifying.QualifyingBase(qLaps) + (rules.QualifyingMode switch {
                        QualifyingPointsMode.EveryRound => awards.Sum(),
                        QualifyingPointsMode.BestRound => awards.DefaultIfEmpty(0).Max(),
                        _ => PositionAward(rules.Qualifying, qPosition) });
                    if (Fastest(quals, driver.Id)) week.Qualifying += rules.Qualifying.FastLapBonus;
                }
                if (rules.Mains.Enabled && (mLaps >= rules.Mains.MinimumLaps && mLaps > 0 || rules.LegacyQualifyingFallback && mOrder.Count == 0 && qLaps > 0))
                {
                    week.Mains = rules.Mains.QualifyingBase(mLaps) + PositionAward(rules.Mains, mPosition);
                    if (Fastest(mains, driver.Id)) week.Mains += rules.Mains.FastLapBonus;
                }
                if (isTopQualifier && qLaps >= rules.Qualifying.MinimumLaps) week.Bonuses += rules.TqBonus;
                if (week.Attended) week.Bonuses += rules.EventAttendancePoints;
                var adjustments = series.Adjustments.Where(a => a.ClassId == classId && a.DriverId == driver.Id && a.EventId == ev.Id).ToList();
                week.Adjustment = adjustments.Sum(a => a.Amount);
                week.Notes = string.Join("; ", adjustments.Select(a => $"{a.Amount:+0.##;-0.##;0}: {a.Note}"));
            }
            // Non-attendance is a zero score and never consumes a counting slot.
            var eligible = row.Weeks.Where(w => !w.Pending && (w.Attended || w.Adjustment != 0)).ToList();
            var kept = eligible.OrderByDescending(w => w.Total).ThenBy(w => w.EventDate).ThenBy(w => w.EventId)
                .Take(series.BestEventCount <= 0 ? eligible.Count : series.BestEventCount).ToList();
            foreach (var week in kept) week.Counted = true;
            row.Events = eligible.Count(w => w.Attended);
            row.CountedEvents = kept.Count;
            row.Wins = kept.Count(w => w.Finish == 1);
            row.BestFinish = kept.Where(w => w.Finish > 0).Select(w => w.Finish).DefaultIfEmpty(int.MaxValue).Min();
            row.ManualPoints = series.Adjustments.Where(a => a.ClassId == classId && a.DriverId == driver.Id && string.IsNullOrEmpty(a.EventId)).Sum(a => a.Amount);
            row.GrossPoints = eligible.Sum(w => w.Total) + row.ManualPoints;
            row.DroppedPoints = eligible.Where(w => !w.Counted).Sum(w => w.Total);
            row.Points = kept.Sum(w => w.Total) + row.ManualPoints;
            rows.Add(row);
        }
        int Compare(SeriesStandingRow a, SeriesStandingRow b)
        {
            var points = b.Points.CompareTo(a.Points);
            if (points != 0) return points;
            if (rules.TieBreak == SeriesTieBreak.BestWeek)
                return b.Weeks.Where(w => w.Counted).Select(w => w.Total).DefaultIfEmpty(0).Max().CompareTo(a.Weeks.Where(w => w.Counted).Select(w => w.Total).DefaultIfEmpty(0).Max());
            if (rules.TieBreak == SeriesTieBreak.MostEvents) return b.Events.CompareTo(a.Events);
            var wins = b.Wins.CompareTo(a.Wins);
            return wins != 0 ? wins : a.BestFinish.CompareTo(b.BestFinish);
        }
        rows.Sort((a,b) => { var c = Compare(a,b); return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(a.DriverName,b.DriverName); });
        for (var i = 0; i < rows.Count; i++) rows[i].Position = i > 0 && Compare(rows[i-1], rows[i]) == 0 ? rows[i-1].Position : i+1;
        return rows;
    }

    private static decimal QualifyingBase(this StagePointsSettings rules, int laps) => rules.ParticipationPoints + rules.PointsPerLap * laps;
}
