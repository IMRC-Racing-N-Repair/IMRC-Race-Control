using System.Text.Json;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;


var legacyFormat = JsonSerializer.Deserialize<ClassDefinition>("{\"RaceFormat\":1}")!;
if (legacyFormat.FormatFor(SessionType.Race) != legacyFormat.RaceFormat) throw new Exception("Legacy mains format changed");
var splitFormat = new ClassDefinition { RaceFormat = RaceFormat.Timed, MainRaceFormat = RaceFormat.Laps };
if (splitFormat.FormatFor(SessionType.Qualifying) != RaceFormat.Timed || splitFormat.FormatFor(SessionType.Race) != RaceFormat.Laps) throw new Exception("Separate formats failed");
var restoredFormat = JsonSerializer.Deserialize<ClassDefinition>(JsonSerializer.Serialize(splitFormat))!;
if (restoredFormat.FormatFor(SessionType.Race) != RaceFormat.Laps) throw new Exception("Mains format did not persist");
var lengthClass = new ClassDefinition { RaceMinutes=5, TargetLaps=50, MainRaceMinutes=8, MainTargetLaps=100 };
if(lengthClass.MinutesFor(SessionType.Qualifying)!=5 || lengthClass.MinutesFor(SessionType.Race)!=8 || lengthClass.LapsFor(SessionType.Race)!=100) throw new Exception("Separate lengths failed");
var roundOverride = new EventSessionDefinition { RaceMinutesOverride=7, RaceFormat=RaceFormat.Timed };
if(JsonSerializer.Deserialize<EventSessionDefinition>(JsonSerializer.Serialize(roundOverride))!.RaceMinutesOverride!=7) throw new Exception("Override persistence failed");
var checks = 0;
void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{label}: expected {expected}, got {actual}");
    checks++;
}
// Completed race exports must stay scoped to saved race cars, including zero-lap drivers.
var exportRace = new EventSessionDefinition { ProgressStatus=EventSessionStatus.Completed, ClassName="Stock", RaceNumber=2,
    Entrants=[new() { CarNumber=1,DriverName="Alex, \"Ace\"" },new() { CarNumber=2,DriverName="Alex, \"Ace\"" },new() { CarNumber=3,DriverName="No laps" }],
    Laps=[new(2,1,"Alex",2,TimeSpan.FromSeconds(5.432),TimeSpan.FromSeconds(10.555),DateTimeOffset.Now,"decoder",true),new(1,1,"Alex",1,TimeSpan.FromSeconds(5.123),TimeSpan.FromSeconds(5.123),DateTimeOffset.Now,"decoder",false),new(3,2,"Alex",1,TimeSpan.FromSeconds(8),TimeSpan.FromSeconds(8),DateTimeOffset.Now,"decoder",false)] };
var exportEvent = new RaceDayEventDefinition { Name="Test event",Sessions=[exportRace] };
var allLapReport = RaceReports.CompletedRace(exportEvent,exportRace);
Equal(3, allLapReport.Sections.Count, "All race drivers including zero laps");
var oneLapReport = RaceReports.CompletedRace(exportEvent,exportRace,1);
Equal(1, oneLapReport.Sections.Count, "One-driver export excludes same-name driver");
Equal(2, oneLapReport.Sections[0].Rows.Count, "One-driver export has all laps");
Equal("1", oneLapReport.Sections[0].Rows[0][0], "Export laps ordered by number");
Equal(RaceEngine.FormatLap(TimeSpan.FromSeconds(5.432)), oneLapReport.Sections[0].Rows[1][1], "Export preserves lap precision");
Equal(RaceEngine.FormatElapsed(TimeSpan.FromSeconds(10.555)), oneLapReport.Sections[0].Rows[1][2], "Export uses recorded elapsed time");
Equal(true, RaceReports.LapReportCsv(oneLapReport).Contains("Alex, \"\"Ace\"\""), "CSV quotes driver commas and quotes");
Equal(true, allLapReport.Sections[2].Notes.Contains("No recorded laps"), "Zero-lap driver explained");
exportRace.ProgressStatus=EventSessionStatus.Planned;
var rejectedIncomplete=false;try { RaceReports.CompletedRace(exportEvent,exportRace); } catch(InvalidOperationException) { rejectedIncomplete=true; }
Equal(true,rejectedIncomplete,"Incomplete race export rejected");
// Live format edits retain history and starts; targets are enforced on subsequent crossings.
var liveEdit = new RaceEngine();
var liveStart = DateTimeOffset.Parse("2026-10-26T12:00:00Z");
var liveCars = new[] { new RacerEntry { CarNumber=1,DriverName="One",Enabled=true }, new RacerEntry { CarNumber=2,DriverName="Two",Enabled=true } };
liveEdit.Start(liveCars,TimeSpan.FromSeconds(10),5,RaceFormat.Timed,TimeSpan.Zero,SessionType.Race,StartStyle.Tone,liveStart);
liveEdit.RegisterPassing(1,"test",liveStart.AddSeconds(5));
liveEdit.RegisterPassing(2,"test",liveStart.AddSeconds(6));
var liveBefore=JsonSerializer.Serialize(liveEdit.Laps);
liveEdit.Tick(liveStart.AddSeconds(11));
Equal(RaceState.TimeExpired,liveEdit.State,"Live edit fixture expired");
liveEdit.UpdateLiveFormat(RaceFormat.Timed,TimeSpan.FromSeconds(20),5,liveStart.AddSeconds(11));
Equal(RaceState.Running,liveEdit.State,"Extending live time clears time-expired state");
Equal(liveBefore,JsonSerializer.Serialize(liveEdit.Laps),"Live time edit preserves exact laps");
Equal(liveStart,liveEdit.MasterStartedAt!.Value,"Live edit preserves master clock");
liveEdit.UpdateLiveFormat(RaceFormat.Laps,TimeSpan.FromSeconds(20),2,liveStart.AddSeconds(12));
Equal(true,liveEdit.RegisterPassing(1,"test",liveStart.AddSeconds(13)).Lap!.Finished,"New lap target finishes leader at crossing");
liveEdit.UpdateLiveFormat(RaceFormat.Timed,TimeSpan.FromSeconds(30),2,liveStart.AddSeconds(14));
Equal(PassingResultCode.AlreadyFinished,liveEdit.RegisterPassing(1,"test",liveStart.AddSeconds(15)).Code,"Live edit preserves finished driver");
liveEdit.UpdateLiveFormat(RaceFormat.Timed,TimeSpan.FromSeconds(8),2,liveStart.AddSeconds(16));
Equal(RaceState.TimeExpired,liveEdit.State,"Shorter live duration expires clock");
Equal(true,liveEdit.RegisterPassing(2,"test",liveStart.AddSeconds(17)).Lap!.Finished,"Shorter time waits for next crossing");
Equal(RaceState.Complete,liveEdit.State,"All finish after format edit");
var rejectedFinishedEdit=false;try { liveEdit.UpdateLiveFormat(RaceFormat.Laps,TimeSpan.FromSeconds(10),10,liveStart.AddSeconds(18)); } catch(InvalidOperationException) { rejectedFinishedEdit=true; }
Equal(true,rejectedFinishedEdit,"Completed live race cannot be edited");
liveEdit.Start(liveCars,TimeSpan.FromSeconds(10),5,RaceFormat.Timed,TimeSpan.Zero,SessionType.Race,StartStyle.IndividualCrossing,liveStart);
liveEdit.UpdateLiveFormat(RaceFormat.Laps,TimeSpan.FromSeconds(10),3,liveStart.AddSeconds(1));
Equal(RaceState.Armed,liveEdit.State,"Armed edit retains individual start behavior");
Equal(0,liveEdit.Laps.Count,"Armed edit does not invent laps");
// Race audio follows elapsed race time; repeats and delayed ticks must not replay cues.
var audioSettings = new AppSettings { RaceLeadersEnabled = true, RaceLeaderCount = 3,
    RaceLeaderIntervalSeconds = 15, RaceHalfwayToneEnabled = true, RaceCountdownEnabled = true,
    RaceCountdownSeconds = 10, RaceTwoMinutesEnabled = true, RaceOneMinuteEnabled = true };
var audio = new RaceAudioSchedule();
Equal(true, audio.FirstCrossing(1), "First crossing cue");
Equal(false, audio.FirstCrossing(1), "No repeated first crossing cue");
Equal("Current leaders are A and B", RaceAudioSchedule.LeaderText(new[] { "A", "B" }, 3), "Only existing leaders");
Equal("Current leader is A", RaceAudioSchedule.LeaderText(new[] { "A", "B" }, 1), "One leader");
Equal("Current leaders are A, B, C, D, and E", RaceAudioSchedule.LeaderText(new[] { "A", "B", "C", "D", "E", "F" }, 5), "Five leader cap");
Equal(true, audio.Advance(15, 180, false, 0, 50, audioSettings).Contains("leaders"), "Leader interval");
Equal(0, audio.Advance(15.1, 180, false, 0, 50, audioSettings).Count, "No duplicate leaders");
Equal(true, audio.Advance(60.05, 180, false, 0, 50, audioSettings).Contains("2 minutes to go"), "Two minutes threshold");
Equal(true, audio.Advance(90, 180, false, 0, 50, audioSettings).Contains("halfway"), "Halfway time");
Equal(true, audio.Advance(120, 180, false, 0, 50, audioSettings).Contains("1 minute to go"), "One minute threshold");
Equal("10", audio.Advance(170.1, 180, false, 0, 50, audioSettings).Single(), "Countdown begins");
Equal(0, audio.Advance(170.2, 180, false, 0, 50, audioSettings).Count, "Countdown once per second");
Equal("7", audio.Advance(173.2, 180, false, 0, 50, audioSettings).Single(), "Delayed tick skips obsolete seconds");
Equal(0, audio.Advance(181, 180, false, 0, 50, audioSettings).Count, "No countdown after expiry");
audio.Reset();
Equal(true, audio.FirstCrossing(1), "Restart resets first crossing");
Equal(true, audio.Advance(1, 0, true, 3, 5, audioSettings).Contains("halfway"), "Lap race halfway rounds up");
Equal(0, audio.Advance(2, 0, true, 4, 5, audioSettings).Count, "Halfway only once");
var audioRestored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(audioSettings))!;
Equal(15, audioRestored.RaceLeaderIntervalSeconds, "Audio preferences persist");
Equal(true, audioRestored.RaceCountdownEnabled, "Countdown preference persists");

// v0.16.6: arbitrary slider intervals and late timer callbacks.
audio.Reset();
audioSettings.RaceLeaderIntervalSeconds = 0;
Equal(false, audio.Advance(30, 180, false, 0, 50, audioSettings).Contains("leaders"), "Zero disables leaders without division by zero");
audio.Reset(); audioSettings.RaceLeaderIntervalSeconds = 7;
Equal(true, audio.Advance(7, 180, false, 0, 50, audioSettings).Contains("leaders"), "Seven-second slider interval");
Equal(true, audio.Advance(14, 180, false, 0, 50, audioSettings).Contains("leaders"), "Seven-second repeat");
audio.Reset();
Equal(true, audio.Advance(64, 180, false, 0, 50, audioSettings).Contains("2 minutes to go"), "Delayed tick retains two-minute cue");
Equal(false, audio.Advance(64.1, 180, false, 0, 50, audioSettings).Contains("2 minutes to go"), "Delayed cue never repeats");
Equal(true, audio.Advance(124, 180, false, 0, 50, audioSettings).Contains("1 minute to go"), "Delayed tick retains one-minute cue");
audio.Reset();
var coincident = audio.Advance(120, 240, false, 0, 50, audioSettings);
Equal(true, coincident.Contains("halfway") && coincident.Contains("2 minutes to go"), "Coincident calls available for combined speech");
audio.Reset();
Equal(true, audio.Advance(0, 120, false, 0, 50, audioSettings).Contains("2 minutes to go"), "Two-minute race boundary");
Equal(true, new AppSettings().RaceHalfwayToneEnabled && new AppSettings().RaceTwoMinutesEnabled && new AppSettings().RaceOneMinuteEnabled, "Time callouts default enabled");

// All seven tone choices persist independently, including silent and long cues.
var toneSettings = new AppSettings { StartTone = TonePattern.AirHorn, FirstCrossingTone = TonePattern.HighPing,
    FirstCrossingToneLength = ToneLength.Long, FirstCrossingToneFile = "first.wav", DriverFinishTone = TonePattern.DoublePulse,
    DriverFinishToneFile = "finish.mp3", HalfwayTone = TonePattern.Halfway, RaceHalfwayToneEnabled = false };
var toneRoundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(toneSettings))!;
Equal(TonePattern.AirHorn, toneRoundTrip.StartTone, "Air horn choice saved");
Equal(TonePattern.HighPing, toneRoundTrip.FirstCrossingTone, "First crossing sound saved");
Equal(ToneLength.Long, toneRoundTrip.FirstCrossingToneLength, "Long duration saved");
Equal("finish.mp3", toneRoundTrip.DriverFinishToneFile, "Driver finish file saved");
audio.Reset();
Equal(true, audio.Advance(90, 180, false, 0, 50, toneRoundTrip).Contains("halfway"), "Halfway tone can run without spoken callout");
toneRoundTrip.HalfwayTone = TonePattern.Silent; audio.Reset();
Equal(false, audio.Advance(90, 180, false, 0, 50, toneRoundTrip).Contains("halfway"), "Silent halfway and disabled speech stays silent");

TimeSpan Lap(decimal seconds) => TimeSpan.FromTicks((long)(seconds * TimeSpan.TicksPerSecond));
foreach (var (seconds, full, digit) in new (decimal, string, string)[] {
    (4.339m, "4 3", "3"), (15.444m, "15 4", "4"), (4.297m, "4 3", "3"),
    (5.0m, "5 0", "0"), (5.1m, "5 1", "1"), (4.2899999m, "4 2", "2"),
    (4.290m, "4 3", "3"), (4.350m, "4 3", "3"), (4.389m, "4 3", "3"),
    (4.390m, "4 4", "4"), (4.990m, "5 0", "0"), (59.999m, "60 0", "0"),
    (0m, "", ""), (-1m, "", "") })
{
    Equal(full, LapAnnouncementFormatter.Format(Lap(seconds), false), $"Full {seconds}");
    Equal(digit, LapAnnouncementFormatter.Format(Lap(seconds), true), $"Tenths {seconds}");
}
var driver = new DriverProfile { FirstName = "Josh", LastName = "Ingersoll", Nickname = "Yoshi" };
Equal("Yoshi", driver.SpokenName, "Nickname fallback");
driver.PhoneticName = " Yoh shee ";
Equal("Yoh shee", driver.SpokenName, "Phonetic override");
Equal("Yoshi", driver.DisplayName, "Unchanged scoring name");
driver.PhoneticName = " "; driver.Nickname = "";
Equal("Josh Ingersoll", driver.SpokenName, "Full name fallback");
var oldNameSetting = JsonSerializer.Deserialize<AppSettings>("{\"LapAnnouncementsIncludeName\":true}")!;
Equal(true, oldNameSetting.AnnounceCheckInNames, "Check-in names enabled when migrating old options");
var legacy = JsonSerializer.Deserialize<AppSettings>("{\"Drivers\":[{\"FirstName\":\"Josh\",\"LastName\":\"Ingersoll\"}]}")!;
Equal(false, legacy.LapAnnouncementsEnabled, "Legacy settings stay quiet");
Equal("Josh Ingersoll", legacy.Drivers[0].SpokenName, "Legacy driver fallback");

// Real engine: exact stored timing survives formatting and rejected crossings
// do not supply a scored LapRecord for the announcement hook.
var engine = new RaceEngine();
var start = DateTimeOffset.Parse("2026-10-02T12:00:00Z");
var racers = new[] { new RacerEntry { CarNumber = 1, DriverId = driver.Id, DriverName = "Josh", Enabled = true } };
engine.Start(racers, TimeSpan.FromMinutes(5), 50, RaceFormat.Timed, Lap(1), SessionType.Qualifying, StartStyle.IndividualCrossing, start);
var crossing = engine.RegisterPassing(1, "test", start);
Equal(PassingResultCode.StartCrossing, crossing.Code, "Start crossing");
Equal<LapRecord?>(null, crossing.Lap, "No start-crossing callout");
var accepted = engine.RegisterPassing(1, "test", start + Lap(4.339m));
Equal(PassingResultCode.LapAccepted, accepted.Code, "Accepted lap");
Equal("4 3", LapAnnouncementFormatter.Format(accepted.Lap!.LapTime, false), "Engine lap callout");
Equal(Lap(4.339m), engine.Laps.Single().LapTime, "Scored milliseconds preserved");
var rejected = engine.RegisterPassing(1, "test", start + Lap(4.5m));
Equal(PassingResultCode.MinimumLapRejected, rejected.Code, "Rejected short lap");
Equal<LapRecord?>(null, rejected.Lap, "No rejected-lap callout");
engine.Stop(start + Lap(5));
Equal(false, engine.RegisterPassing(1, "test", start + Lap(9)).Accepted, "No post-stop lap");

// Finish decoration must follow each driver's clock AND final crossing, never master expiry alone.
var finishEngine = new RaceEngine();
finishEngine.Start(new[] { new RacerEntry { CarNumber=1, DriverName="A", Enabled=true }, new RacerEntry { CarNumber=2, DriverName="B", Enabled=true } },
    TimeSpan.FromSeconds(10), 50, RaceFormat.Timed, TimeSpan.FromSeconds(1), SessionType.Qualifying, StartStyle.IndividualCrossing, start);
finishEngine.RegisterPassing(1, "test", start);
finishEngine.RegisterPassing(2, "test", start.AddSeconds(5));
finishEngine.Tick(start.AddSeconds(11));
Equal(false, finishEngine.GetStandings(start.AddSeconds(11)).Any(r => r.IsFinished), "Expiry alone never highlights position");
Equal(true, finishEngine.RegisterPassing(1, "test", start.AddSeconds(11)).Lap!.Finished, "First driver final crossing");
Equal(false, finishEngine.RegisterPassing(2, "test", start.AddSeconds(11)).Lap!.Finished, "Later starter still racing");
Equal(1, finishEngine.GetStandings(start.AddSeconds(11)).Count(r => r.IsFinished), "Only completed driver's position marked");
Equal(true, finishEngine.RegisterPassing(2, "test", start.AddSeconds(16)).Lap!.Finished, "Later starter finishes after own clock");
finishEngine.UndoLastPassing(start.AddSeconds(16));
Equal(false, finishEngine.GetStandings(start.AddSeconds(16)).Single(r => r.CarNumber == 2).IsFinished, "Undo removes completed outline");

// Master clock starts at START, even when personal scoring waits for a crossing.
foreach (var style in new[] { StartStyle.Tone, StartStyle.IndividualCrossing, StartStyle.LeaderCrossing })
{
    var master = new RaceEngine();
    master.Start(racers, TimeSpan.FromSeconds(10), 50, RaceFormat.Timed, TimeSpan.Zero, SessionType.Qualifying, style, start);
    Equal(TimeSpan.FromSeconds(7), master.GetRemaining(start.AddSeconds(3)), "Master runs before crossing " + style);
    Equal(TimeSpan.FromSeconds(-2), master.GetRemaining(start.AddSeconds(12)), "Master goes negative " + style);
    if (style != StartStyle.Tone)
    {
        master.RegisterPassing(1, "test", start.AddSeconds(3));
        Equal(TimeSpan.FromSeconds(10), master.GetRacerRemaining(1, start.AddSeconds(3))!.Value, "Personal clock starts at crossing " + style);
        Equal(TimeSpan.FromSeconds(7), master.GetRemaining(start.AddSeconds(3)), "Crossing never resets master " + style);
    }
    master.Stop(start.AddSeconds(12));
    Equal(TimeSpan.FromSeconds(-2), master.GetRemaining(start.AddSeconds(30)), "Stopped master freezes " + style);
    master.Reset();
    Equal<DateTimeOffset?>(null, master.MasterStartedAt, "Reset clears master " + style);
}
Equal("-0:04.2", RaceEngine.FormatMasterClock(TimeSpan.FromMilliseconds(-4200)), "Signed overtime display");
Equal("0:00.0", RaceEngine.FormatMasterClock(TimeSpan.Zero), "Exact zero display");
var completionClock = new RaceEngine();
completionClock.Start(racers, TimeSpan.FromSeconds(10), 50, RaceFormat.Timed, TimeSpan.Zero, SessionType.Qualifying, StartStyle.IndividualCrossing, start);
completionClock.RegisterPassing(1, "test", start.AddSeconds(3));
completionClock.RegisterPassing(1, "test", start.AddSeconds(14));
Equal(TimeSpan.FromSeconds(-4), completionClock.GetRemaining(start.AddSeconds(20)), "Completed master freezes at final crossing");
completionClock.UndoLastPassing(start.AddSeconds(20));
Equal(TimeSpan.FromSeconds(-10), completionClock.GetRemaining(start.AddSeconds(20)), "Undo resumes original master");
completionClock.FalseStart();
Equal<DateTimeOffset?>(null, completionClock.MasterStartedAt, "False start clears master");

// Never touch a real Race Control database when this check is run on Windows.
// Verify the application's snapshot serializer and the per-driver JSON contract.
driver.PhoneticName = "Yoh shee";
var settings = new AppSettings { LapAnnouncementsEnabled = true, LapAnnouncementsTenthsOnly = true,
    AnnounceCheckInNames = true, LapSpeechRate = 3, LapSpeechVolume = 75, Drivers = [driver] };
var saved = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
Equal(true, saved.LapAnnouncementsEnabled, "Enable persisted");
Equal(true, saved.LapAnnouncementsTenthsOnly, "Tenths persisted");
Equal(true, saved.AnnounceCheckInNames, "Name setting persisted");
Equal(3, saved.LapSpeechRate, "Speed persisted");
Equal(75, saved.LapSpeechVolume, "Volume persisted");
Equal("Yoh shee", saved.Drivers[0].SpokenName, "Phonetics persisted");

// This optional integration check requires an isolated data directory on Linux.
if (args.Contains("--isolated-database"))
{
    var db = new RaceControlDatabaseService();
    if (!db.DatabasePath.StartsWith("/tmp/imrc-callout-test-data/", StringComparison.Ordinal))
        throw new Exception("Refusing to use a non-isolated database.");
    await db.SaveApplicationStateAsync(settings);
    var loaded = (await new RaceControlDatabaseService().LoadApplicationStateAsync())!;
    Equal(true, loaded.LapAnnouncementsTenthsOnly, "SQLite setting reload");
    Equal("Yoh shee", loaded.Drivers[0].PhoneticName, "SQLite phonetic reload");
}
// Separate dashboard switches and selected voice survive upgrade and save/reload.
var preferences = new AppSettings { LapAnnouncementsEnabled = true };
Equal(true, preferences.PracticeLapCalloutsEnabled, "Practice inherits previous race preference");
preferences.PracticeLapAnnouncementsEnabled = false;
Equal(true, preferences.LapAnnouncementsEnabled, "Practice off leaves race on");
Equal(false, preferences.PracticeLapCalloutsEnabled, "Practice explicitly off");
preferences.LapAnnouncementsEnabled = false;
preferences.PracticeLapAnnouncementsEnabled = true;
preferences.LapSpeechVoice = "Test installed voice";
var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(preferences))!;
Equal(false, restored.LapAnnouncementsEnabled, "Race switch saved independently");
Equal(true, restored.PracticeLapCalloutsEnabled, "Practice switch saved independently");
Equal("Test installed voice", restored.LapSpeechVoice, "Selected voice saved");
Equal(true, restored.AnnounceCheckInNames, "Check-in names independent of lap switch");
Equal("", legacy.LapSpeechVoice, "Legacy default voice");

// Exercise the same queue consumed by the Windows speech service.
SpeechCallout Call(string text, long at, bool isLap = true, int age = 500) =>
    new(text, 6, 100, at, isLap, "", age);
var queue = new SpeechCalloutQueue();
queue.Enqueue(Call("4 1", 0), true, LapSpeechBusyMode.LatestOnly);
queue.Enqueue(Call("4 2", 100), true, LapSpeechBusyMode.LatestOnly);
Equal(1, queue.PendingLaps, "Only one lap may wait");
Equal(1, queue.SkippedLaps, "Replaced stale lap counted");
Equal(true, queue.TryDequeue(150, out var latest), "Latest lap available");
Equal("4 2", latest.Text, "Latest arrival replaces older lap");
queue.Enqueue(Call("4 3", 200), true, LapSpeechBusyMode.LatestOnly);
Equal(false, queue.TryDequeue(701, out _), "Old lap expires before speaking");
queue.Enqueue(Call("4 4", 800), true, LapSpeechBusyMode.SkipWhenBusy);
Equal(0, queue.PendingLaps, "Skip mode never queues behind speech");
queue.Enqueue(Call("Yoh shee", 900, false), false, LapSpeechBusyMode.LatestOnly);
queue.Enqueue(Call("4 5", 900), true, LapSpeechBusyMode.LatestOnly);
queue.ClearLaps();
Equal(true, queue.TryDequeue(5000, out var name), "Lap toggle preserves waiting check-in name");
Equal("Yoh shee", name.Text, "Name preserved");
queue.Enqueue(Call("old lap", 6000), true, LapSpeechBusyMode.LatestOnly);
queue.Enqueue(Call("new lap", 6100), true, LapSpeechBusyMode.SkipWhenBusy);
Equal(0, queue.PendingLaps, "Switching to skip mode clears previous waiting lap");
queue.Enqueue(Call("lap", 6200), true, LapSpeechBusyMode.LatestOnly);
queue.Enqueue(Call("name", 6200, false), true, LapSpeechBusyMode.LatestOnly);
queue.Clear();
Equal(false, queue.TryDequeue(6200, out _), "End/stop clears every pending call");

// Ten minutes of 8 cars / four-second laps (1,200 crossings), with both evenly
// spaced crossings and 8-car bursts. Simulated slow speech takes 650 ms/call.
foreach (var mode in Enum.GetValues<LapSpeechBusyMode>())
foreach (var burst in new[] { false, true })
{
    var load = new SpeechCalloutQueue();
    long? endsAt = null;
    int offered = 0, starts = 0, maxPending = 0;
    void PumpAt(long now)
    {
        if (endsAt is null && load.TryDequeue(now, out var call))
        {
            if (now - call.QueuedAt > 500) throw new Exception("Stale call started");
            starts++;
            endsAt = now + 650;
        }
    }
    for (int crossingIndex = 0; crossingIndex < 1200; crossingIndex++)
    {
        long now = burst ? (crossingIndex / 8) * 4000L : crossingIndex * 500L;
        while (endsAt.HasValue && endsAt.Value <= now)
        {
            long finished = endsAt.Value;
            endsAt = null;
            PumpAt(finished);
        }
        load.Enqueue(Call("4 3", now), endsAt.HasValue, mode);
        offered++;
        maxPending = Math.Max(maxPending, load.PendingLaps);
        PumpAt(now);
    }
    Equal(true, maxPending <= 1, $"Bounded queue under {mode}, burst={burst}");
    Equal(true, load.SkippedLaps > 0, $"Overload drops calls under {mode}, burst={burst}");
    Equal(offered, starts + load.SkippedLaps + load.PendingLaps, "Every offered lap accounted for");
    load.Clear(); // Same queue operation used by service Stop() at race completion.
    endsAt = null; // Service also cancels its sole active speech prompt.
    Equal(false, load.TryDequeue(600000, out _), "No post-race backlog");
}
var speechOptions = new AppSettings { LapSpeechRate = 10, NameSpeechRate = -3,
    LapSpeechBusyMode = LapSpeechBusyMode.SkipWhenBusy, LapSpeechMaxAgeMilliseconds = 250 };
var speechReload = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(speechOptions))!;
Equal(10, speechReload.LapSpeechRate, "Fast number speed saved");
Equal(-3, speechReload.NameSpeechRate, "Slow name speed saved independently");
Equal(LapSpeechBusyMode.SkipWhenBusy, speechReload.LapSpeechBusyMode, "Busy mode saved");
Equal(250, speechReload.LapSpeechMaxAgeMilliseconds, "Max call age saved");
Equal(0, legacy.NameSpeechRate, "Legacy upgrade starts name speed at normal");
Equal(500, legacy.LapSpeechMaxAgeMilliseconds, "Default call age is half a second");


// Reopen and edit a saved round while retaining other rounds and completed history.
var firstDriver = new DriverProfile { FirstName = "First" };
var lateDriver = new DriverProfile { FirstName = "Late", PhoneticName = "Layt" };
var completedHeat = new EventSessionDefinition { ClassId = "class-a", Round = 1, Heat = 1,
    ProgressStatus = EventSessionStatus.Completed, CompletedAt = start,
    Results = [new StandingRow { CarNumber = 1, DriverName = "Winner", Position = 1, Laps = 42 }] };
var pending = new EventSessionDefinition { ClassId = "class-a", Round = 1, Heat = 2,
    ProgressStatus = EventSessionStatus.Loaded,
    Entrants = [new SessionEntrant { CarNumber = 1, DriverId = firstDriver.Id, DriverName = "First" }] };
var otherRound = new EventSessionDefinition { ClassId = "class-a", Round = 2 };
var otherClass = new EventSessionDefinition { ClassId = "class-b", Round = 1 };
var raceDay = new RaceDayEventDefinition { ClassIds = ["class-a", "class-b"],
    Sessions = [completedHeat, pending, otherRound, otherClass] };
var completedHeatJson = JsonSerializer.Serialize(completedHeat);
var edited = JsonSerializer.Deserialize<EventSessionDefinition>(JsonSerializer.Serialize(pending))!;
edited.Entrants.Insert(0, new SessionEntrant { CarNumber = 1, DriverId = lateDriver.Id,
    DriverName = "Late", TransponderId = "123456", SeedRank = 8 });
edited.Entrants[1].CarNumber = 2;
Equal(1, pending.Entrants.Count, "Preview additions do not mutate original before save");
Equal(true, SavedRoundEditor.PreservesCompletedResults([completedHeat, pending], [completedHeat, edited]), "Mixed round can be saved");
Equal(false, SavedRoundEditor.PreservesCompletedResults([completedHeat], []), "Completed heat cannot disappear");
var completedClone = JsonSerializer.Deserialize<EventSessionDefinition>(completedHeatJson)!;
Equal(false, SavedRoundEditor.PreservesCompletedResults([completedHeat], [completedClone]), "Matching ID cannot bypass completed lock");
try
{
    SavedRoundEditor.ReplaceScope(raceDay, [completedHeat, pending], [edited]);
    throw new Exception("Completed history removal unexpectedly succeeded");
}
catch (InvalidOperationException) { checks++; }
Equal(4, raceDay.Sessions.Count, "Failed save leaves all races intact");
SavedRoundEditor.ReplaceScope(raceDay, [completedHeat, pending], [completedHeat, edited]);
Equal(completedHeatJson, JsonSerializer.Serialize(completedHeat), "Completed result unchanged");
Equal(true, raceDay.Sessions.Contains(otherRound) && raceDay.Sessions.Contains(otherClass), "Other scopes untouched");
Equal(pending.Id, edited.Id, "Edited race identity retained");
Equal(EventSessionStatus.Loaded, edited.ProgressStatus, "Loaded race remains loaded");
Equal(lateDriver.Id, edited.Entrants[0].DriverId, "New lineup order saved");
Equal(2, edited.Entrants[1].CarNumber, "Original driver renumbered");
Equal("123456", raceDay.DriverAssignments.Single(a => a.DriverId == lateDriver.Id).TransponderId, "Late arrival registered for next round");
SavedRoundEditor.ReplaceScope(raceDay, [completedHeat, edited], [completedHeat, edited]);
Equal(2, raceDay.DriverAssignments.Count, "Repeated saves do not duplicate event entries");

if (args.Contains("--isolated-database"))
{
    var db = new RaceControlDatabaseService();
    if (!db.DatabasePath.StartsWith("/tmp/imrc-callout-test-data/", StringComparison.Ordinal))
        throw new Exception("Refusing to use a non-isolated database.");
    var state = new AppSettings { Events = [raceDay], Drivers = [firstDriver, lateDriver] };
    await db.SaveApplicationStateAsync(state);
    var reopenedState = (await new RaceControlDatabaseService().LoadApplicationStateAsync())!;
    var reopenedEvent = reopenedState.Events.Single();
    var reopened = reopenedEvent.Sessions.Single(s => s.Id == pending.Id);
    Equal(2, reopened.Entrants.Count, "SQLite reopen keeps added driver");
    Equal(lateDriver.Id, reopened.Entrants[0].DriverId, "SQLite reopen keeps lineup order");
    Equal("123456", reopened.Entrants[0].TransponderId, "SQLite reopen keeps transponder");
    Equal(8, reopened.Entrants[0].SeedRank, "SQLite reopen keeps seed");
    Equal("Layt", reopenedState.Drivers.Single(d => d.Id == lateDriver.Id).PhoneticName, "New profile retained on restart");
    Equal(completedHeatJson, JsonSerializer.Serialize(reopenedEvent.Sessions.Single(s => s.Id == completedHeat.Id)), "Completed history survives restart");
    // Return after exiting, edit again, and save a second time.
    var secondEdit = JsonSerializer.Deserialize<EventSessionDefinition>(JsonSerializer.Serialize(reopened))!;
    secondEdit.Entrants.RemoveAt(0);
    secondEdit.Entrants[0].CarNumber = 1;
    SavedRoundEditor.ReplaceScope(reopenedEvent, [reopened], [secondEdit]);
    await db.SaveApplicationStateAsync(reopenedState);
    var twice = (await new RaceControlDatabaseService().LoadApplicationStateAsync())!.Events.Single();
    Equal(1, twice.Sessions.Single(s => s.Id == pending.Id).Entrants.Count, "Second edit survives second restart");
    Equal(firstDriver.Id, twice.Sessions.Single(s => s.Id == pending.Id).Entrants.Single().DriverId, "Correct remaining driver");
    Equal(2, twice.DriverAssignments.Count, "Race removal does not delete event registration");
    Equal(4, twice.Sessions.Count, "Repeated edit does not duplicate heats");
}
checks += LapFinishChecks.Run();
checks += EligibilityPrintChecks.Run();
checks += await SeriesChecks.Run(args.Contains("--isolated-database"));
checks += await RaceScheduleChecks.Run(args.Contains("--isolated-database"));
checks += DashboardWorkflowChecks.Run();
checks += await DecoderChecks.Run();
checks += await TrackmateChecks.Run();
checks += NextRaceChecks.Run();
checks += await V140Checks.Run(args.Contains("--isolated-database"));
checks += await V141Checks.Run();
checks += await V143Checks.Run(args.Contains("--isolated-database"));
checks += await V142Checks.Run(args.Contains("--isolated-database"));
checks += await OnlineChecks.Run();
checks += await DataProtectionChecks.Run();
checks += await RentalTransponderChecks.Run(args.Contains("--isolated-database"));
Console.WriteLine($"PASS: {checks} speech, race-engine, round editing, Series, report, decoder and persistence checks.");
