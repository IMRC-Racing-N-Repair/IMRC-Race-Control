using System.Text.Json;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;

static class V142Checks
{
    public static async Task<int> Run(bool isolated)
    {
        var count = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); count++; }
        var now = DateTimeOffset.UtcNow;
        var racers = new[] { new RacerEntry { CarNumber = 1, DriverName = "A" }, new RacerEntry { CarNumber = 2, DriverName = "B" } };
        var engine = new RaceEngine();
        void Start() => engine.Start(racers, TimeSpan.FromMinutes(1), 1, RaceFormat.Laps, TimeSpan.FromSeconds(1), SessionType.Race, StartStyle.Tone, now);
        Start();
        Check(engine.GetStandings(now).All(r => !r.IsFinished), "New race has no finished-driver color");
        engine.RegisterPassing(1, "test", now.AddSeconds(4));
        var rows = engine.GetStandings(now.AddSeconds(4));
        Check(rows.Single(r => r.CarNumber == 1).IsFinished && !rows.Single(r => r.CarNumber == 2).IsFinished, "One driver finishes while opponent stays active");
        var live = new StandingRow(); var notices = 0;
        live.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(StandingRow.IsFinished)) notices++; };
        live.CopyTimingFrom(rows.Single(r => r.CarNumber == 1));
        Check(live.IsFinished && notices == 1, "Timing copy notifies finished style immediately");
        live.IsLastPassing = true;
        Check(live.IsFinished && live.IsLastPassing, "Final crossing highlight and finished state coexist");
        Start(); live.CopyTimingFrom(engine.GetStandings(now).First());
        Check(!live.IsFinished && notices == 2, "Restart removes finished-driver state");
        engine.Stop(now.AddSeconds(1));
        Check(engine.GetStandings(now.AddSeconds(1)).All(r => !r.IsFinished), "Early stopped racer is not falsely marked finished");
        var card = new PracticeCardViewModel(); var flashed = 0;
        card.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PracticeCardViewModel.IsLastPassing)) flashed++; };
        card.IsLastPassing = true; card.IsLastPassing = true; card.IsLastPassing = false;
        Check(flashed == 2, "Shared practice card reports flash transitions once");
        var old = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Check(old.DriverCrossingStyle == "Box" && old.DriverCrossingColor == "#35C76F", "Upgrade defaults to green crossing box");
        Check(old.FinishedDriverStyle == "Row" && old.FinishedDriverColor == "#808080", "Upgrade defaults to grey finished row");
        var settings = new AppSettings { DriverCrossingStyle = "Underline", DriverCrossingColor = "#00AABB", FinishedDriverStyle = "Text", FinishedDriverColor = "#AABBCC" };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Check(restored.DriverCrossingStyle == "Underline" && restored.DriverCrossingColor == "#00AABB" && restored.FinishedDriverStyle == "Text" && restored.FinishedDriverColor == "#AABBCC", "Presentation preferences survive JSON roundtrip");
        if (isolated)
        {
            if (!Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).StartsWith("/tmp/imrc-callout-test-data")) throw new Exception("Isolated data directory required");
            var db = new RaceControlDatabaseService(); await db.InitializeAsync(); await db.SaveApplicationStateAsync(settings);
            var loaded = (await db.LoadApplicationStateAsync())!;
            Check(loaded.DriverCrossingStyle == "Underline" && loaded.FinishedDriverStyle == "Text" && loaded.FinishedDriverColor == "#AABBCC", "Display preferences survive SQLite reopen");
        }
        return count;
    }
}
