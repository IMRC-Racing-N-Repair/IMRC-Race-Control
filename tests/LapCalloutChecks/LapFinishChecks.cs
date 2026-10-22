using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

internal static class LapFinishChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var now = DateTimeOffset.Parse("2026-10-19T12:00:00Z");
        var racers = new[] { new RacerEntry { CarNumber = 1, Enabled = true }, new RacerEntry { CarNumber = 2, Enabled = true }, new RacerEntry { CarNumber = 3, Enabled = true } };
        foreach (var style in new[] { StartStyle.Tone, StartStyle.LeaderCrossing, StartStyle.IndividualCrossing })
        {
            var e = new RaceEngine();
            e.Start(racers, TimeSpan.FromMinutes(5), 3, RaceFormat.Laps, TimeSpan.FromSeconds(1), SessionType.Race, style, now);
            if (style == StartStyle.LeaderCrossing) e.RegisterPassing(1, "test", now);
            if (style == StartStyle.IndividualCrossing)
                foreach (var r in racers) e.RegisterPassing(r.CarNumber, "test", now);
            e.RegisterPassing(1, "test", now.AddSeconds(2));
            e.RegisterPassing(2, "test", now.AddSeconds(3));
            e.RegisterPassing(1, "test", now.AddSeconds(4));
            e.RegisterPassing(3, "test", now.AddSeconds(5));
            Check(!e.LapTargetReached, "No finish before target");
            var winner = e.RegisterPassing(1, "test", now.AddSeconds(6));
            Check(winner.Lap?.Finished == true && e.LapTargetReached, "Winner finishes at target");
            Check(e.State == RaceState.Running && !e.AllDriversFinished, "Wait for remaining crossings");
            Check(e.GetStandings(now.AddSeconds(6)).Where(r => r.CarNumber != 1).All(r => r.Status == "FINAL LAP"), "Lapped racers shown on final lap");
            Check(!e.RegisterPassing(3, "test", now.AddSeconds(5.5)).Accepted, "Rejected crossing cannot finish racer");
            var second = e.RegisterPassing(2, "test", now.AddSeconds(7));
            Check(second.Lap?.Finished == true && second.Lap.LapNumber == 2, "Lapped driver finishes next crossing below target");
            Check(!e.RegisterPassing(1, "test", now.AddSeconds(8)).Accepted, "Winner cannot accumulate extra laps");
            Check(e.RegisterPassing(3, "test", now.AddSeconds(9)).Lap?.Finished == true, "Last driver finishes next crossing");
            Check(e.AllDriversFinished && e.State == RaceState.Complete, "Race completes automatically");
            Check(e.GetMasterElapsed(now.AddSeconds(100)) == TimeSpan.FromSeconds(9), "Master clock freezes at last finish");
            Check(!e.RegisterPassing(2, "test", now.AddSeconds(10)).Accepted, "Completed race ignores crossings");
            e.UndoLastPassing(now.AddSeconds(10));
            Check(!e.AllDriversFinished && e.State == RaceState.Running, "Undo last finish reopens race");
            e.UndoLastPassing(now.AddSeconds(10));
            e.UndoLastPassing(now.AddSeconds(10));
            Check(!e.LapTargetReached && e.GetStandings(now.AddSeconds(10)).All(r => !r.IsFinished), "Undo target resets shared finish");
            e.Reset();
            Check(!e.LapTargetReached && !e.AllDriversFinished, "Reset clears finish trigger");
        }
        var late = new RaceEngine();
        late.Start(racers.Take(2), TimeSpan.FromMinutes(5), 1, RaceFormat.Laps, TimeSpan.FromSeconds(1), SessionType.Qualifying, StartStyle.IndividualCrossing, now);
        late.RegisterPassing(1, "test", now);
        late.RegisterPassing(1, "test", now.AddSeconds(2));
        late.RegisterPassing(2, "test", now.AddSeconds(3));
        Check(late.AllDriversFinished && late.State == RaceState.Complete, "Unstarted entrant finishes at first crossing after target");
        Check(late.GetStandings(now.AddSeconds(3)).Single(r => r.CarNumber == 2).Laps == 0, "Unstarted entrant receives no phantom lap");
        return checks;
    }
}
