using System.Text;
using System.Text.Json;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;

static class V140Checks
{
    public static async Task<int> Run(bool isolated)
    {
        var count = 0;
        void Check(bool result, string text) { if (!result) throw new Exception(text); count++; }
        void Near(double expected, double actual, string text) => Check(Math.Abs(expected - actual) < .000001, text + $" ({expected} vs {actual})");
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var enhanced = Encoding.ASCII.GetBytes("\x01@\t100\t10\t1234567\t37.589\t130\t119\t2\txEB94\r\n");
        var auto = new AutomaticDecoder();
        var hit = auto.Feed(enhanced, now).Passings.Single();
        Check(hit.TransponderId == "1234567" && hit.ClockMicroseconds == 37589000, "Documented enhanced passing");
        Check(hit.Protocol == "AMB enhanced" && auto.Protocol.Contains("enhanced"), "Automatic enhanced identification");
        for (var i = 1; i < enhanced.Length - 2; i++)
        {
            var parser = new AutomaticDecoder();
            Check(parser.Feed(enhanced.AsSpan(0, i), now).Passings.Count == 0, $"Enhanced partial {i}");
            Check(parser.Feed(enhanced.AsSpan(i), now).Passings.Count == 1, $"Enhanced split {i}");
        }
        Check(auto.Feed(enhanced, now).Passings.Count == 0, "Enhanced duplicate rejected");
        auto.ResetConnection();
        Check(auto.Feed(enhanced, now).Passings.Count == 0, "Enhanced reconnect duplicate rejected");
        var corrupt = (byte[])enhanced.Clone(); corrupt[12] ^= 1;
        Check(new AutomaticDecoder().Feed(corrupt, now).Passings.Count == 0, "Enhanced corrupt checksum rejected");
        var amb20 = Encoding.ASCII.GetBytes("@0102030405\r\n");
        hit = new AutomaticDecoder().Feed(amb20, now).Passings.Single();
        Check(hit.TransponderId == "1" && hit.ClockMicroseconds == 7384050000, "AMB20 documented ID and hundredth-second clock");
        var batch = new AutomaticDecoder().Feed(Encoding.ASCII.GetBytes("@00000001\r\n@0100000100\r\n@0100000500\r\n"), now).Passings;
        Check(batch.Count == 2, "AMB20 init is not a crossing");
        Near(4, (batch[1].Timestamp - batch[0].Timestamp).TotalSeconds, "AMB20 batch retains intervals");
        Check(new AutomaticDecoder().Feed(Encoding.ASCII.GetBytes("@0100990405\r\n1234567\r\n"), now).Passings.Count == 0, "Bad clock and arbitrary IDs rejected");
        var p3 = Convert.FromHexString("8E023300CF02000001000104B29B0100030427FC70000408E819E6BD8A7504000502330006021000080200008104FC0504008F");
        auto = new();
        Check(auto.Feed(p3, now).Passings.Single().TransponderId == "7404583" && auto.Protocol.Contains("P3"), "Auto P3 retained");
        auto.ResetConnection();
        Check(auto.Feed(amb20, now).Passings.Single().Protocol == "AMB20", "Can change decoder protocol on reconnect");
        var isolatedBinary = new byte[] { 0x8e }.Concat(amb20).Concat(new byte[] { 0x8f }).ToArray();
        Check(new AutomaticDecoder().Feed(isolatedBinary, now).Passings.Count == 0, "Text inside binary frame cannot become AMB20 passing");
        Check(new AutomaticDecoder().Feed(Encoding.ASCII.GetBytes(new string('x', 512) + "@0102030405\r\n"), now).Passings.Count == 0, "Overlong line suffix cannot manufacture a crossing");
        auto = new(); auto.Feed(amb20, now);
        Check(auto.Feed(Encoding.ASCII.GetBytes("@00000001\r\n").Concat(amb20).ToArray(), now.AddHours(1)).Passings.Count == 1, "AMB20 clock reset starts a fresh deduplication epoch");
        var cards = new List<PracticeCardViewModel>();
        var simulation = new AutomaticDecoder();
        var simFirst = simulation.Feed(SimulatorPassing.Encode(7654321, 0, now, 20, 80), now).Passings.Single();
        var simSecond = simulation.Feed(SimulatorPassing.Encode(7654321, 5, now.AddSeconds(3.5), 21, 81), now.AddSeconds(3.5)).Passings.Single();
        Check(simFirst.TransponderId == "7654321" && simFirst.Hits == 20, "Simulator emits a recognized passing");
        Near(3.5, (simSecond.Timestamp - simFirst.Timestamp).TotalSeconds, "Simulator preserves lap interval");
        var simCards = new List<PracticeCardViewModel>();
        PracticeTracker.Record(simCards, null, simFirst.TransponderId, "Simulator", simFirst.Timestamp);
        PracticeTracker.Record(simCards, null, simSecond.TransponderId, "Simulator", simSecond.Timestamp);
        Check(simCards.Single().DriverName == "Unknown driver" && simCards[0].LapCount == 1, "Simulator reaches unknown practice lap counting");
        var first = PracticeTracker.Record(cards, null, "123", "decoder", now);
        Check(first.Created && first.Card.DriverName == "Unknown driver" && first.Card.LapCount == 0, "Unknown first crossing creates card without fabricated lap");
        var lap = PracticeTracker.Record(cards, null, "123", "decoder", now.AddSeconds(4.339));
        Check(lap.LapAdded && lap.Card.LapCount == 1, "Unknown driver counts laps");
        Near(4.339, lap.Card.Laps[0].LapTime.TotalSeconds, "Unknown driver lap precision");
        PracticeTracker.Record(cards, null, "456", "decoder", now.AddSeconds(1));
        Check(cards.Count == 2 && cards[0].DriverId != cards[1].DriverId, "Unknown tags have independent cards");
        Check(!PracticeTracker.Record(cards, null, "123", "decoder", now.AddSeconds(4.4)).LapAdded, "Immediate duplicate practice read ignored");
        Check(!PracticeTracker.Record(cards, null, "123", "decoder", now).LapAdded, "Backwards practice crossing ignored");
        var a = new DriverProfile { FirstName = "Yoshi" };
        var b = new DriverProfile { FirstName = "Clint" };
        var c = new DriverProfile { FirstName = "New racer" };
        var id = a.Uuid;
        Check(Guid.TryParse(id, out _), "Default driver has UUID");
        a.Nickname = "New nickname";
        Check(a.Uuid == id, "Nickname does not change UUID");
        var legacy = new DriverProfile { Id = "legacy-driver", FirstName = "Legacy" };
        Check(Guid.TryParse(legacy.Uuid, out _) && JsonSerializer.Deserialize<DriverProfile>(JsonSerializer.Serialize(legacy))!.Uuid == legacy.Uuid, "Legacy UUID deterministic and reopen-safe");
        var resolved = PracticeTracker.Record(cards, a, "123", "decoder", now.AddSeconds(8.678));
        Check(resolved.DriverResolved && resolved.Card.DriverId == a.Id && resolved.Card.LapCount == 2 && cards.Count == 2, "Live unknown assignment preserves laps");
        resolved.Card.IsSaving = true;
        Check(PracticeTracker.Record(cards, a, "123", "decoder", now.AddSeconds(13)).Created, "Crossing during save starts new stint");
        var settings = new DriverRatingSettings();
        EventSessionDefinition Race(string classId = "c1", int entrants = 2)
        {
            var race = new EventSessionDefinition { ClassId = classId, ClassName = classId, SessionType = SessionType.Race, ProgressStatus = EventSessionStatus.Completed, StartedAt = now };
            var profiles = new[] { a, b, c };
            for (var car = 1; car <= entrants; car++)
            {
                race.Entrants.Add(new() { CarNumber = car, DriverId = profiles[car - 1].Id, DriverName = profiles[car - 1].DisplayName });
                race.Results.Add(new() { CarNumber = car, Position = car, Laps = 20 });
                for (var n = 1; n <= 20; n++) race.Laps.Add(new(race.Laps.Count + 1, car, profiles[car - 1].DisplayName, n, TimeSpan.FromSeconds(car * 4), TimeSpan.FromSeconds(n * car * 4), now.AddSeconds(n * car * 4), "test", n == 20));
            }
            return race;
        }
        var race1 = Race();
        var ev = new RaceDayEventDefinition { EventDate = now.Date, Sessions = [race1] };
        DriverRatingResult Calculate() => DriverRatingCalculator.Calculate([ev], [a, b, c], settings);
        var ratings = Calculate();
        Check(ratings.Standings.Count == 2, "Only actual racers receive rankings");
        Near(1516, ratings.Standings.Single(r => r.DriverId == a.Id).Rating, "Equal-rated winner receives 16");
        Near(1484, ratings.Standings.Single(r => r.DriverId == b.Id).Rating, "Equal-rated loser loses 16");
        Near(1500, ratings.History[0].StrengthOfField, "Initial SOF uses pre-race ratings");
        Near(5 - 200d / 119, ratings.Standings[0].Safety, "Safety clean-lap prior and first-lap exclusion");
        Near(ratings.Standings[0].Safety, ratings.Standings[1].Safety, "Slower consistent driver is not penalized for pace");
        Near(100, ratings.History[0].Consistency!.Value, "Consistent race gives 100 percent");
        Check(ratings.Standings.All(r => r.Tier == "Provisional" && r.SafetyTier == "Provisional"), "Early ratings marked provisional");
        Near(ratings.Standings[0].Rating, Calculate().Standings[0].Rating, "Recalculation does not award twice");
        Near(1516, DriverRatingCalculator.Calculate([ev, ev], [a, b], settings).Standings[0].Rating, "Duplicate event references not double-counted");
        race1.Laps[0] = race1.Laps[0] with { LapTime = TimeSpan.FromSeconds(50) };
        Near(ratings.Standings[0].Safety, Calculate().Standings[0].Safety, "Slow launch excluded from safety");
        race1.Laps[5] = race1.Laps[5] with { LapTime = TimeSpan.FromSeconds(8) };
        var incidentRatings = Calculate();
        Check(incidentRatings.Standings.Single(r => r.DriverId == a.Id).EstimatedIncidents == 1, "Large slow lap becomes estimated incident");
        Check(incidentRatings.Standings.Single(r => r.DriverId == a.Id).Safety < ratings.Standings[0].Safety, "Estimated incident reduces safety");
        var review = new DriverRatingReview { EventId = ev.Id, SessionId = race1.Id, DriverId = a.Id, ExcludeSafety = true, Note = "Missed read" };
        var reviewed = DriverRatingCalculator.Calculate([ev], [a, b], settings, [review]);
        Near(3, reviewed.Standings.Single(r => r.DriverId == a.Id).Safety, "Reviewed exclusion removes safety contribution");
        Near(1516, reviewed.Standings.Single(r => r.DriverId == a.Id).Rating, "Safety exclusion leaves performance unchanged");
        Check(reviewed.History.Single(r => r.DriverId == a.Id).Note == "Missed read", "Review note retained");
        race1.Results[0].Position = 2; race1.Results[1].Position = 1;
        Near(1484, Calculate().Standings.Single(r => r.DriverId == a.Id).Rating, "Result correction rebuilds rating");
        race1.Results[0].Position = race1.Results[1].Position = 1;
        Near(1500, Calculate().Standings[0].Rating, "Tie produces no equal-rating transfer");
        race1.ProgressStatus = EventSessionStatus.Loaded;
        Check(Calculate().Standings.Count == 0, "Restarted/incomplete race removed from rankings");
        race1.ProgressStatus = EventSessionStatus.Completed; race1.SessionType = SessionType.Qualifying;
        Check(Calculate().Standings.Count == 0, "Qualifying excluded");
        race1.SessionType = SessionType.Practice;
        Check(Calculate().Standings.Count == 0, "Practice excluded");
        race1.SessionType = SessionType.Race; race1.Results[1].Laps = 0;
        Check(Calculate().Standings.Count == 0, "One-car/DNS field excluded");
        race1 = Race(); ev.Sessions = [race1];
        var next = Race(); next.StartedAt = now.AddHours(1); next.Entrants[1].DriverId = c.Id;
        ev.Sessions.Add(next);
        var twoRaces = Calculate();
        Near(1508, twoRaces.History.First(r => r.SessionId == next.Id).StrengthOfField, "Next field uses earned pre-race rating");
        var other = Race("c2"); ev.Sessions.Add(other);
        Near(1516, Calculate().Standings.Single(r => r.ClassId == "c2" && r.DriverId == a.Id).Rating, "Classes have independent ratings");
        var stored = new AppSettings { LastDataBits = "8", LastParity = "None", LastStopBits = "2", LastHandshake = "RequestToSend", Drivers = [a, b, c], Events = [ev], DriverRatingReviews = [review], DriverRatingSettings = settings };
        var reopened = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(stored))!;
        Check(reopened.Drivers[0].Uuid == a.Uuid && reopened.DriverRatingReviews[0].Note == "Missed read", "JSON identity and review roundtrip");
        if (isolated)
        {
            if (!Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).StartsWith("/tmp/imrc-callout-test-data")) throw new Exception("Database tests require isolated XDG_DATA_HOME");
            var db = new RaceControlDatabaseService(); await db.InitializeAsync(); await db.SaveApplicationStateAsync(stored);
            var loaded = (await db.LoadApplicationStateAsync())!;
            Check(loaded.Drivers[0].Uuid == a.Uuid && loaded.DriverRatingReviews[0].ExcludeSafety, "SQLite identity and review roundtrip");
            Check(loaded.LastStopBits == "2" && loaded.LastHandshake == "RequestToSend", "Serial framing preferences persist");
            var practice = new PracticeHistoryService();
            var unknown = new List<PracticeCardViewModel>(); PracticeTracker.Record(unknown, null, "987654", "test", now);
            PracticeTracker.Record(unknown, null, "987654", "test", now.AddSeconds(5));
            await practice.SaveStintAsync(unknown[0], now.AddSeconds(6));
            var saved = (await practice.LoadRecentStintsAsync(driverId: unknown[0].DriverId)).Single(s => s.StintId == unknown[0].StintId);
            Check(saved.DriverName == "Unknown driver" && saved.TransponderId == "987654" && saved.LapCount == 1, "Unknown practice persists without registering fake driver");
            Check((await practice.LoadLapsAsync(saved.StintId)).Count == 1, "Unknown practice lap detail restored");
        }
        return count;
    }
}
