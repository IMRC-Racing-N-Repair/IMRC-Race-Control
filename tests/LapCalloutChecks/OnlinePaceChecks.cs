using System.Text.Json;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

internal static class OnlinePaceChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("Online pace: " + message); checks++; }
        var track = new TrackProfile();
        var cls = new ClassDefinition { Id = "stock", Name = "Stock", RaceMinutes = 4.25, MainRaceMinutes = 6.5, TargetLaps = 23, MainTargetLaps = 60 };
        var session = new EventSessionDefinition { ClassId = cls.Id, ClassName = cls.Name, RaceNumber = 1, StartStyle = StartStyle.Tone,
            Entrants = [new() { CarNumber = 7, DriverName = "Driver A" }, new() { CarNumber = 12, DriverName = "Waiting driver" }] };
        var ev = new RaceDayEventDefinition { Track = track.Snapshot(), Sessions = [session] };
        var engine = new RaceEngine();
        var start = DateTimeOffset.Parse("2026-11-27T12:00:00Z");
        List<ClassDefinition> classes = [cls];
        JsonElement Races(bool live = false, bool completedOnly = false) =>
            OnlineSnapshotBuilder.Capture(track, [ev], [], [], classes, ev.Id, CombinedRaces.Run(ev.Sessions, session).Id,
                engine.State, engine.GetStandings(start.AddSeconds(30)), engine.Laps, live, completedOnly, engine.TimingSettings)
                .Documents.Single().Value.GetProperty("races");
        JsonElement Race(bool live = false, bool completedOnly = false) => Races(live, completedOnly)[0];
        JsonElement Driver(JsonElement race, int car) => race.GetProperty("drivers").EnumerateArray().Single(d => d.GetProperty("carNumber").GetInt32() == car);
        string? Pace(JsonElement race, int car = 7) => Driver(race, car).GetProperty("pace").GetString();
        void Timed(JsonElement race, double seconds)
        {
            Check(race.GetProperty("raceFormat").GetString() == "timed", "timed format");
            Check(Math.Abs(race.GetProperty("durationSeconds").GetDouble() - seconds) < 0.000001, "effective timed duration");
            Check(!race.TryGetProperty("targetLaps", out _), "timed race has no lap target");
        }
        void Lapped(JsonElement race, int target)
        {
            Check(race.GetProperty("raceFormat").GetString() == "laps", "lap-count format");
            Check(race.GetProperty("targetLaps").GetInt32() == target, "effective lap target");
            Check(!race.TryGetProperty("durationSeconds", out _), "lap race has no duration");
        }
        void Start(RaceTimingSettings timing, EventSessionDefinition run)
        {
            engine.Start(run.Entrants.Select(e => new RacerEntry { CarNumber = e.CarNumber, DriverName = e.DriverName, ScoringGroupId = e.SourceSessionId }),
                timing.Duration, timing.TargetLaps, timing.Format, TimeSpan.FromSeconds(1), run.SessionType, run.StartStyle, start);
        }
        void SaveCompletion(EventSessionDefinition run)
        {
            run.ProgressStatus = EventSessionStatus.Completed;
            run.RaceMinutesOverride = engine.Duration.TotalMinutes;
            run.RaceFormat = engine.RaceFormat;
            run.TargetLaps = engine.TargetLaps;
            run.Results = engine.GetStandings(start.AddSeconds(40)).ToList();
            run.Laps = engine.Laps.ToList();
            CombinedRaces.WriteBack(run);
        }

        Timed(Race(), 255);
        Check(!Driver(Race(), 7).TryGetProperty("pace", out _), "planned driver has no projection");
        session.SessionType = SessionType.Race;
        Timed(Race(), 390);
        session.RaceMinutesOverride = 5;
        Timed(Race(), 300);
        Start(RaceTimingSettings.ForSession(session, cls), session);
        Check(Pace(Race(true)) == "—", "armed/running with zero laps has no pace");

        // The tone-to-first-crossing interval is not a scored lap. Exact decoder
        // tick precision matters: rounding the average to milliseconds first is wrong.
        var baseline = start.AddSeconds(2);
        engine.RegisterPassing(7, "decoder", baseline);
        var firstLap = baseline.AddTicks(66_306_889);
        engine.RegisterPassing(7, "decoder", firstLap);
        var oneLap = Race(true);
        Timed(oneLap, 300);
        Check(oneLap.GetProperty("status").GetString() == "live", "live race");
        Check(Driver(oneLap, 7).GetProperty("laps").GetInt32() == 1, "one completed lap");
        Check(Pace(oneLap) == "45/4:58.381", "one-lap projection uses full precision and not master elapsed");
        Check(Pace(oneLap) == engine.GetStandings(firstLap).Single(r => r.CarNumber == 7).Pace, "one-lap pace matches desktop");
        Check(Pace(oneLap, 12) == "—", "waiting driver has no projection");

        // The actual active engine can differ from the saved schedule (e.g. a
        // dashboard duration edit or a live format/target change).
        engine.UpdateLiveFormat(RaceFormat.Timed, TimeSpan.FromSeconds(240), 50, firstLap);
        Timed(Race(true), 240);
        Check(Pace(Race(true)) == "36/3:58.704", "live engine duration overrides stale scheduled duration");
        var secondLap = firstLap.AddTicks(73_693_111); // exactly 14 seconds across two scored laps
        engine.RegisterPassing(7, "decoder", secondLap);
        Check(Pace(Race(true)) == "34/3:58.000", "projection averages completed laps");
        engine.UpdateLiveFormat(RaceFormat.Laps, TimeSpan.FromSeconds(240), 20, secondLap);
        Lapped(Race(true), 20);
        Check(Pace(Race(true)) == "20/2:20.000", "live lap target pace");
        Check(Pace(Race(true)) == engine.GetStandings(secondLap).Single(r => r.CarNumber == 7).Pace, "lap pace matches desktop");

        engine.UpdateLiveFormat(RaceFormat.Timed, TimeSpan.FromSeconds(240), 20, secondLap);
        engine.Stop(secondLap.AddSeconds(1));
        var justFinished = Race(true);
        Timed(justFinished, 240);
        Check(justFinished.GetProperty("status").GetString() == "completed" && Pace(justFinished) == "34/3:58.000", "completed engine upload before persistence");
        SaveCompletion(session);
        engine.Reset();
        cls.MainRaceMinutes = 12;
        var saved = Race(false, true);
        Timed(saved, 240);
        Check(saved.GetProperty("status").GetString() == "completed" && Pace(saved) == session.Results.Single(r => r.CarNumber == 7).Pace, "saved completed upload ignores later class changes");
        Check(Pace(saved) == "34/3:58.000", "completed-only keeps projected pace, not the recorded lap count");

        session.ProgressStatus = EventSessionStatus.Planned;
        session.RaceFormat = RaceFormat.Laps;
        session.TargetLaps = cls.LapsFor(session.SessionType);
        Lapped(Race(), 60);
        session.TargetLaps = 0;
        Lapped(Race(), 23); // Start race uses cls.TargetLaps for an invalid session target.
        session.TargetLaps = 20;
        Start(RaceTimingSettings.ForSession(session, cls), session);
        engine.RegisterPassing(7, "decoder", baseline);
        engine.RegisterPassing(7, "decoder", baseline.AddSeconds(7));
        engine.Stop(baseline.AddSeconds(8));
        SaveCompletion(session);
        engine.Reset();
        Lapped(Race(false, true), 20);
        Check(Pace(Race(false, true)) == "20/2:20.000", "saved lap-count race projects target finish time");

        // No made-up duration/target for missing or invalid legacy configuration.
        session.RaceFormat = RaceFormat.Timed;
        session.RaceMinutesOverride = null;
        classes.Clear();
        var missing = Race();
        Check(missing.GetProperty("raceFormat").GetString() == "timed" && !missing.TryGetProperty("durationSeconds", out _)
            && Pace(missing) == "—", "missing duration is not guessed");
        classes.Add(cls);
        foreach (var invalid in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity, double.MaxValue })
        {
            session.RaceMinutesOverride = invalid;
            var bad = Race();
            Check(!bad.TryGetProperty("durationSeconds", out _) && Pace(bad) == "—", "invalid override is not replaced by class default");
        }
        session.RaceFormat = RaceFormat.Laps;
        session.TargetLaps = 0;
        cls.TargetLaps = -1;
        Check(!Race().TryGetProperty("targetLaps", out _) && Pace(Race()) == "—", "invalid lap target");
        session.RaceFormat = (RaceFormat)99;
        Check(!Race().TryGetProperty("raceFormat", out _) && Pace(Race()) == "—", "unknown race format");

        // Combined heats have overlapping original car numbers; runtime rows/laps
        // must follow the source session and car mapping, not the driver name.
        session.RaceFormat = RaceFormat.Timed;
        session.RaceMinutesOverride = 4;
        session.TargetLaps = 50;
        session.ProgressStatus = EventSessionStatus.Planned;
        session.Results.Clear(); session.Laps.Clear();
        session.CombinedRaceId = "combined"; session.CombinedRaceOrder = 1;
        var otherCls = new ClassDefinition { Id = "modified", Name = "Modified", MainRaceMinutes = 4 };
        classes.Add(otherCls);
        var other = new EventSessionDefinition { ClassId = otherCls.Id, ClassName = otherCls.Name, SessionType = session.SessionType,
            StartStyle = session.StartStyle, RaceMinutesOverride = 4, CombinedRaceId = "combined", CombinedRaceOrder = 2, RaceNumber = 2,
            Entrants = [new() { CarNumber = 7, DriverName = "Driver A" }] };
        ev.Sessions.Add(other);
        var combined = CombinedRaces.Run(ev.Sessions, session);
        Start(RaceTimingSettings.ForSession(combined, cls), combined);
        var aCar = CombinedRaces.TrackCar(ev.Sessions, session, 7);
        var bCar = CombinedRaces.TrackCar(ev.Sessions, other, 7);
        Check(aCar != bCar && bCar != 7, "duplicate source car numbers mapped to unique runtime cars");
        engine.RegisterPassing(aCar, "decoder", baseline);
        engine.RegisterPassing(bCar, "decoder", baseline);
        engine.RegisterPassing(aCar, "decoder", baseline.AddSeconds(6));
        engine.RegisterPassing(bCar, "decoder", baseline.AddSeconds(8));
        var liveRaces = Races(true);
        Timed(liveRaces[0], 240); Timed(liveRaces[1], 240);
        Check(Pace(liveRaces[0]) == "40/4:00.000" && Pace(liveRaces[1]) == "30/4:00.000", "combined live pace uses each mapped driver's laps");
        Check(Driver(liveRaces[0], 7).GetProperty("lapTimes")[0].GetDouble() == 6 && Driver(liveRaces[1], 7).GetProperty("lapTimes")[0].GetDouble() == 8, "combined live lap arrays use same mapping as pace");
        engine.Stop(baseline.AddSeconds(9));
        SaveCompletion(combined);
        engine.Reset();
        ev.Sessions = JsonSerializer.Deserialize<List<EventSessionDefinition>>(JsonSerializer.Serialize(ev.Sessions))!;
        session = ev.Sessions[0];
        var savedRaces = Races(false, true);
        Check(Pace(savedRaces[0]) == "40/4:00.000" && Pace(savedRaces[1]) == "30/4:00.000", "combined completed pace survives writeback and reload");
        Check(Driver(savedRaces[1], 7).GetProperty("lapTimes")[0].GetDouble() == 8, "saved source car lap data correct");
        return checks;
    }
}
