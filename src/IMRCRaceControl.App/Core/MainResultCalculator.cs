using IMRCRaceControl.Models;
namespace IMRCRaceControl.Core;
public static class MainResultCalculator
{
    public sealed record MainLegPerformance(
        SessionEntrant Entrant,
        int Leg,
        int Position,
        int Laps,
        double ElapsedSeconds,
        double BestLapSeconds);

    public sealed class MainSeriesStanding
    {
        public required SessionEntrant Entrant { get; init; }
        public required List<MainLegPerformance> Counted { get; init; }
        public int Points { get; init; }
        public int Position { get; set; }
    }

    public static List<MainSeriesStanding> CalculateMainSeriesStandings(
        IReadOnlyList<EventSessionDefinition> series)
    {
        var completed = series
            .Where(s => s.ProgressStatus == EventSessionStatus.Completed && s.Results.Count > 0)
            .OrderBy(s => s.MainLeg)
            .ThenBy(s => s.QueueOrder)
            .ToList();

        if (completed.Count == 0)
            return [];

        var configuredBest = completed
            .Select(s => Math.Max(1, s.MainCountBest))
            .DefaultIfEmpty(1)
            .First();
        var countBest = Math.Clamp(configuredBest, 1, completed.Count);

        var entrants = series
            .SelectMany(s => s.Entrants)
            .Where(e => !string.IsNullOrWhiteSpace(e.DriverId))
            .GroupBy(e => e.DriverId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var standings = new List<MainSeriesStanding>();

        foreach (var entrant in entrants)
        {
            var performances = new List<MainLegPerformance>();

            foreach (var leg in completed)
            {
                var legEntrant = leg.Entrants.FirstOrDefault(e =>
                    string.Equals(e.DriverId, entrant.DriverId, StringComparison.OrdinalIgnoreCase));

                var penaltyPosition = Math.Max(leg.Entrants.Count, leg.Results.Count) + 1;
                if (legEntrant is null)
                {
                    performances.Add(new MainLegPerformance(
                        entrant,
                        Math.Max(1, leg.MainLeg),
                        penaltyPosition,
                        0,
                        double.MaxValue,
                        double.MaxValue));
                    continue;
                }

                var result = leg.Results.FirstOrDefault(r => r.CarNumber == legEntrant.CarNumber);
                if (result is null)
                {
                    performances.Add(new MainLegPerformance(
                        legEntrant,
                        Math.Max(1, leg.MainLeg),
                        penaltyPosition,
                        0,
                        double.MaxValue,
                        double.MaxValue));
                    continue;
                }

                var laps = leg.Laps
                    .Where(l => l.CarNumber == legEntrant.CarNumber)
                    .OrderBy(l => l.LapNumber)
                    .ToList();

                performances.Add(new MainLegPerformance(
                    legEntrant,
                    Math.Max(1, leg.MainLeg),
                    result.Position > 0 ? result.Position : penaltyPosition,
                    result.Laps,
                    laps.Count > 0 ? laps[^1].Elapsed.TotalSeconds : double.MaxValue,
                    laps.Count > 0 ? laps.Min(l => l.LapTime.TotalSeconds) : double.MaxValue));
            }

            if (performances.Count == 0)
                continue;

            // IFMAR-style scoring: finishing position equals points. Keep the
            // configured best N runs. Laps/time are used only as tie-break data.
            var counted = performances
                .OrderBy(p => p.Position)
                .ThenByDescending(p => p.Laps)
                .ThenBy(p => p.ElapsedSeconds)
                .ThenBy(p => p.Leg)
                .Take(Math.Min(countBest, performances.Count))
                .ToList();

            standings.Add(new MainSeriesStanding
            {
                Entrant = entrant,
                Counted = counted,
                Points = counted.Sum(p => p.Position)
            });
        }

        standings.Sort(CompareMainSeriesStanding);
        for (var i = 0; i < standings.Count; i++)
            standings[i].Position = i + 1;

        return standings;
    }

    private static int CompareMainSeriesStanding(
        MainSeriesStanding a,
        MainSeriesStanding b)
    {
        var pointsCompare = a.Points.CompareTo(b.Points);
        if (pointsCompare != 0)
            return pointsCompare;

        // IFMAR tie-break concept: compare the single best counted finish first,
        // then the second-best counted finish. If positions are still tied,
        // compare laps/time from those same counted finishes.
        var aCounted = a.Counted
            .OrderBy(p => p.Position)
            .ThenByDescending(p => p.Laps)
            .ThenBy(p => p.ElapsedSeconds)
            .ToList();
        var bCounted = b.Counted
            .OrderBy(p => p.Position)
            .ThenByDescending(p => p.Laps)
            .ThenBy(p => p.ElapsedSeconds)
            .ToList();

        var count = Math.Max(aCounted.Count, bCounted.Count);
        for (var i = 0; i < count; i++)
        {
            if (i >= aCounted.Count) return 1;
            if (i >= bCounted.Count) return -1;

            var positionCompare = aCounted[i].Position.CompareTo(bCounted[i].Position);
            if (positionCompare != 0)
                return positionCompare;
        }

        for (var i = 0; i < Math.Min(aCounted.Count, bCounted.Count); i++)
        {
            var lapCompare = bCounted[i].Laps.CompareTo(aCounted[i].Laps);
            if (lapCompare != 0)
                return lapCompare;

            var elapsedCompare = aCounted[i].ElapsedSeconds.CompareTo(bCounted[i].ElapsedSeconds);
            if (elapsedCompare != 0)
                return elapsedCompare;

            var bestLapCompare = aCounted[i].BestLapSeconds.CompareTo(bCounted[i].BestLapSeconds);
            if (bestLapCompare != 0)
                return bestLapCompare;
        }

        var seedCompare = (a.Entrant.SeedRank <= 0 ? int.MaxValue : a.Entrant.SeedRank)
            .CompareTo(b.Entrant.SeedRank <= 0 ? int.MaxValue : b.Entrant.SeedRank);
        if (seedCompare != 0)
            return seedCompare;

        return string.Compare(
            a.Entrant.DisplayName,
            b.Entrant.DisplayName,
            StringComparison.OrdinalIgnoreCase);
    }

}
