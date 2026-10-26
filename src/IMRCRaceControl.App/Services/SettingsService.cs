using System.IO;
using System.Text.Json;
using IMRCRaceControl.Models;
using IMRCRaceControl.Core;

namespace IMRCRaceControl.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string SettingsPath { get; } = Path.Combine(ApplicationDataPaths.DataDirectory,
        "settings.json");

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(SettingsPath)) return CreateDefaults();
        await using var stream = File.OpenRead(SettingsPath);
        var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream,JsonOptions)
            ?? throw new InvalidDataException("The settings backup could not be read. It has not been overwritten.");
        Normalize(loaded);
        return loaded;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Normalize(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            await using (var stream = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream,settings,JsonOptions);
                await stream.FlushAsync();
            }
            File.Move(temporary,SettingsPath,true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static AppSettings CreateDefaults()
    {
        var settings = new AppSettings { SettingsSchemaVersion = 1100 };
        Normalize(settings);
        return settings;
    }

    public static void Normalize(AppSettings settings)
    {
        settings.RentalTransponders ??= new();
        IMRCRaceControl.Core.RentalTransponderRegistry.ValidateState(settings.RentalTransponders);
        if (settings.DriverCrossingStyle is not ("Box" or "Underline")) settings.DriverCrossingStyle = "Box";
        if (settings.FinishedDriverStyle is not ("Row" or "Text" or "None")) settings.FinishedDriverStyle = "Row";
        if (!ThemeService.IsValidColor(settings.DriverCrossingColor)) settings.DriverCrossingColor = "#35C76F";
        if (!ThemeService.IsValidColor(settings.FinishedDriverColor)) settings.FinishedDriverColor = "#808080";
        settings.Classes ??= [];
        settings.Drivers ??= [];
        settings.DriverRatingSettings ??= new();
        settings.DriverRatingReviews ??= [];
        settings.DriverClassAssignments ??= [];
        settings.Track ??= new();
        settings.OnlinePublishing ??= new();
        if (string.IsNullOrWhiteSpace(settings.Track.Id)) settings.Track.Id = Guid.NewGuid().ToString("N");
        settings.Events ??= [];
        settings.Series ??= [];
        settings.Racers ??= [];


        if (settings.Classes.Count == 0)
        {
            settings.Classes.Add(new ClassDefinition
            {
                Name = string.IsNullOrWhiteSpace(settings.ClassName) ? "Open" : settings.ClassName,
                RaceMinutes = settings.RaceMinutes > 0 ? settings.RaceMinutes : 5,
                MinimumLapSeconds = settings.MinimumLapSeconds >= 0 ? settings.MinimumLapSeconds : 1,
                QualStartStyle = StartStyle.IndividualCrossing,
                RaceStartStyle = StartStyle.Tone
            });
        }

        foreach (var cls in settings.Classes)
        {
            if (!Enum.IsDefined(cls.RaceFormat))
                cls.RaceFormat = RaceFormat.Timed;

            if (cls.RaceMinutes <= 0)
                cls.RaceMinutes = 5;

            if (cls.TargetLaps is < 1 or > 10000)
                cls.TargetLaps = 50;

            if (cls.MaxRacersPerHeat is < 1 or > 30)
                cls.MaxRacersPerHeat = 19;

            if (cls.MaxRacersPerMain is < 1 or > 30)
                cls.MaxRacersPerMain = cls.MaxRacersPerHeat is >= 1 and <= 30
                    ? cls.MaxRacersPerHeat
                    : 19;

            cls.MainRules ??= [];
            if (cls.MainRules.Count == 0)
            {
                cls.MainRules.Add(new MainRuleDefinition
                {
                    Order = 1,
                    MaxRacers = cls.MaxRacersPerMain,
                    BumpInsFromLower = 0,
                    RunCount = 1,
                    CountBest = 1
                });
            }

            var normalizedRules = cls.MainRules
                .OrderBy(r => r.Order)
                .Take(26)
                .ToList();

            cls.MainRules = normalizedRules;
            for (var i = 0; i < cls.MainRules.Count; i++)
            {
                var rule = cls.MainRules[i];
                rule.Order = i + 1;
                rule.MaxRacers = Math.Clamp(rule.MaxRacers, 1, 30);
                rule.BumpInsFromLower = Math.Clamp(
                    rule.BumpInsFromLower,
                    0,
                    Math.Max(0, rule.MaxRacers - 1));
                rule.RunCount = Math.Clamp(rule.RunCount, 1, 10);
                rule.CountBest = Math.Clamp(rule.CountBest, 1, rule.RunCount);
            }

            // The lowest configured main has no lower main feeding it.
            cls.MainRules[^1].BumpInsFromLower = 0;
        }

        // One-time migrations from earlier development builds.
        if (settings.SettingsSchemaVersion < 301)
            RemoveGeneratedPlaceholderDrivers(settings);

        if (settings.SettingsSchemaVersion < 400)
        {
            // v0.3.x always created a default "Club Race" event. v0.4 starts with no
            // event until the race director explicitly creates one. Remove only the
            // unmistakable untouched placeholder; preserve any event with sessions or edits.
            if (settings.Events.Count == 1)
            {
                var only = settings.Events[0];
                if (string.Equals(only.Name, "Club Race", StringComparison.OrdinalIgnoreCase) &&
                    (only.Sessions?.Count ?? 0) == 0)
                {
                    settings.Events.Clear();
                }
            }

            settings.SettingsSchemaVersion = 400;
        }

        if (settings.SettingsSchemaVersion < 500)
        {
            // v0.5 adds durable event/session lifecycle and embedded race history.
            settings.SettingsSchemaVersion = 500;
        }

        if (settings.SettingsSchemaVersion < 600)
        {
            // v0.6 adds an event-specific roster so race-day entries no longer depend
            // on a driver's permanent class registration. Existing events are migrated
            // from their heat entrants first, then from permanent driver/class defaults.
            foreach (var ev in settings.Events)
            {
                ev.DriverAssignments ??= [];

                if (ev.DriverAssignments.Count == 0)
                {
                    foreach (var session in ev.Sessions ?? [])
                    {
                        foreach (var entrant in session.Entrants ?? [])
                        {
                            if (string.IsNullOrWhiteSpace(entrant.DriverId) ||
                                string.IsNullOrWhiteSpace(session.ClassId) ||
                                ev.DriverAssignments.Any(a => a.DriverId == entrant.DriverId && a.ClassId == session.ClassId))
                                continue;

                            ev.DriverAssignments.Add(new EventDriverAssignment
                            {
                                DriverId = entrant.DriverId,
                                ClassId = session.ClassId,
                                TransponderId = entrant.TransponderId ?? string.Empty,
                                SeedRank = entrant.SeedRank
                            });
                        }
                    }

                    foreach (var classId in ev.ClassIds ?? [])
                    {
                        var grouped = settings.DriverClassAssignments
                            .Where(a => a.ClassId == classId)
                            .GroupBy(a => a.DriverId);

                        foreach (var group in grouped)
                        {
                            if (ev.DriverAssignments.Any(a => a.DriverId == group.Key && a.ClassId == classId))
                                continue;

                            var transponders = group
                                .Select(a => (a.TransponderId ?? string.Empty).Trim())
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Distinct(StringComparer.OrdinalIgnoreCase);
                            var ranks = group.Select(a => a.SeedRank).Where(r => r > 0).ToArray();

                            ev.DriverAssignments.Add(new EventDriverAssignment
                            {
                                DriverId = group.Key,
                                ClassId = classId,
                                TransponderId = string.Join(";", transponders),
                                SeedRank = ranks.Length == 0 ? 0 : ranks.Min()
                            });
                        }
                    }
                }
            }

            settings.SettingsSchemaVersion = 600;
        }

        if (settings.SettingsSchemaVersion < 700)
        {
            // v0.7 adds optional first/last names and checklist-based race-day setup.
            // New profile fields deserialize as empty strings; existing driver identities remain unchanged.
            settings.SettingsSchemaVersion = 700;
        }

        if (settings.SettingsSchemaVersion < 800)
        {
            // v0.8 adds operator themes and four independent audio cues.
            settings.SettingsSchemaVersion = 800;
        }

        if (settings.SettingsSchemaVersion < 801)
        {
            // v0.8.1 removes the separate Driver Name field.
            // First name is now required, last name is optional, and nickname
            // becomes the scoring display only when it is not blank.
            foreach (var driver in settings.Drivers)
            {
                driver.FirstName ??= string.Empty;
                driver.LastName ??= string.Empty;
                driver.Nickname ??= string.Empty;
                driver.Name ??= string.Empty;

                if (string.IsNullOrWhiteSpace(driver.FirstName))
                {
                    var legacyName = driver.Name.Trim();

                    if (!string.IsNullOrWhiteSpace(legacyName))
                    {
                        var parts = legacyName
                            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        if (parts.Length > 0)
                            driver.FirstName = parts[0];

                        if (parts.Length > 1 && string.IsNullOrWhiteSpace(driver.LastName))
                            driver.LastName = string.Join(" ", parts.Skip(1));
                    }
                    else if (!string.IsNullOrWhiteSpace(driver.Nickname))
                    {
                        // Defensive fallback for any malformed old development record.
                        driver.FirstName = driver.Nickname.Trim();
                    }
                }

                driver.Name = $"{driver.FirstName} {driver.LastName}".Trim();
            }

            settings.SettingsSchemaVersion = 801;
        }

        if (settings.SettingsSchemaVersion < 806)
        {
            // v0.8.6 adds custom audio files and persistent Race Dashboard
            // visibility/order/color preferences. Existing users keep every
            // dashboard field visible in the same order they had before.
            settings.DashboardVisibleColumns = DashboardColumnCatalog.OptionalColumns.ToList();
            settings.DashboardColumnOrder = DashboardColumnCatalog.OptionalColumns.ToList();
            settings.SettingsSchemaVersion = 806;
        }

        if (settings.SettingsSchemaVersion < 807)
        {
            // v0.8.7 gives each application theme an explicit dashboard palette.
            // Existing blank dashboard colors become the selected theme's defaults.
            // Existing custom colors are preserved and stop following the theme.
            var defaults = ThemeService.GetDashboardDefaults(settings.Theme);
            var hasCustomDashboardColor =
                !string.IsNullOrWhiteSpace(settings.DashboardBackgroundColor) ||
                !string.IsNullOrWhiteSpace(settings.DashboardPanelColor) ||
                !string.IsNullOrWhiteSpace(settings.DashboardRowColor) ||
                !string.IsNullOrWhiteSpace(settings.DashboardTextColor) ||
                !string.IsNullOrWhiteSpace(settings.DashboardPassingHighlightColor);

            if (!hasCustomDashboardColor)
            {
                settings.DashboardBackgroundColor = defaults.Background;
                settings.DashboardPanelColor = defaults.Panel;
                settings.DashboardRowColor = defaults.Row;
                settings.DashboardAlternateRowColor = defaults.AlternateRow;
                settings.DashboardTextColor = defaults.Text;
                settings.DashboardPassingHighlightColor = defaults.PassingHighlight;
                settings.DashboardFastestLapHighlightColor = defaults.FastestLapHighlight;
                settings.DashboardNewBestLapHighlightColor = defaults.NewBestLapHighlight;
                settings.DashboardColorsFollowTheme = true;
            }
            else
            {
                settings.DashboardAlternateRowColor = defaults.AlternateRow;
                settings.DashboardFastestLapHighlightColor = defaults.FastestLapHighlight;
                settings.DashboardNewBestLapHighlightColor = defaults.NewBestLapHighlight;
                settings.DashboardColorsFollowTheme = ThemeService.DashboardColorsMatchDefaults(
                    settings.Theme,
                    settings.DashboardBackgroundColor,
                    settings.DashboardPanelColor,
                    settings.DashboardRowColor,
                    settings.DashboardAlternateRowColor,
                    settings.DashboardTextColor,
                    settings.DashboardPassingHighlightColor,
                    settings.DashboardFastestLapHighlightColor,
                    settings.DashboardNewBestLapHighlightColor,
                    defaults.CheckInGlow,
                    defaults.MinimumLapGlow,
                    defaults.PositionUp,
                    defaults.PositionDown,
                    defaults.PositionSame);
            }

            settings.SettingsSchemaVersion = 807;
        }

        if (settings.SettingsSchemaVersion < 808)
        {
            // v0.8.8 adds alternating driver rows plus dedicated highlights
            // for the session-fastest driver and a newly-reset personal best.
            var defaults = ThemeService.GetDashboardDefaults(settings.Theme);

            if (settings.DashboardColorsFollowTheme)
            {
                settings.DashboardAlternateRowColor = defaults.AlternateRow;
                settings.DashboardFastestLapHighlightColor = defaults.FastestLapHighlight;
                settings.DashboardNewBestLapHighlightColor = defaults.NewBestLapHighlight;
            }
            else
            {
                if (!ThemeService.IsValidColor(settings.DashboardAlternateRowColor))
                    settings.DashboardAlternateRowColor = defaults.AlternateRow;
                if (!ThemeService.IsValidColor(settings.DashboardFastestLapHighlightColor))
                    settings.DashboardFastestLapHighlightColor = defaults.FastestLapHighlight;
                if (!ThemeService.IsValidColor(settings.DashboardNewBestLapHighlightColor))
                    settings.DashboardNewBestLapHighlightColor = defaults.NewBestLapHighlight;
            }

            settings.DashboardAlternateRowsEnabled = true;
            settings.SettingsSchemaVersion = 808;
        }

        if (settings.SettingsSchemaVersion < 811)
        {
            // v0.8.11 adds pre-race driver check-in status and its configurable
            // name-glow color. Existing dashboard palettes keep all prior colors.
            var defaults = ThemeService.GetDashboardDefaults(settings.Theme);
            settings.DashboardCheckInGlowColor = defaults.CheckInGlow;
            settings.SettingsSchemaVersion = 811;
        }

        if (settings.SettingsSchemaVersion < 812)
        {
            // v0.8.12 retains below-minimum crossings for breakout review and
            // adds an operator-configurable temporary orange name glow.
            var defaults = ThemeService.GetDashboardDefaults(settings.Theme);

            if (!ThemeService.IsValidColor(settings.DashboardMinimumLapGlowColor))
                settings.DashboardMinimumLapGlowColor = defaults.MinimumLapGlow;

            if (settings.DashboardMinimumLapGlowSeconds <= 0 ||
                settings.DashboardMinimumLapGlowSeconds > 30)
            {
                settings.DashboardMinimumLapGlowSeconds = 1.0;
            }

            settings.SettingsSchemaVersion = 812;
        }

        if (settings.SettingsSchemaVersion < 900)
        {
            // v0.9 introduces SQLite-backed practice history and persistent
            // championship/series definitions. Practice history is stored in
            // RaceControl.db; only series definitions belong in settings JSON.
            settings.Series ??= [];
            settings.LastSeriesId ??= string.Empty;

            foreach (var series in settings.Series)
            {
                series.EventIds ??= [];
                if (string.IsNullOrWhiteSpace(series.PointsCsv))
                    series.PointsCsv = RaceSeriesDefinition.DefaultPointsCsv;
            }

            settings.SettingsSchemaVersion = 900;
        }

        if (settings.SettingsSchemaVersion < 901)
        {
            // v0.9.1 moves the complete application/racing state into SQLite.
            // settings.json remains only as a fallback/migration backup.
            settings.SettingsSchemaVersion = 901;
        }

        if (settings.SettingsSchemaVersion < 903)
        {
            // v0.9.3 adds configurable Live Practice statistics and keeps
            // completed race selection under explicit Previous/Next control.
            settings.PracticeVisibleStats = PracticeStatCatalog.Defaults.ToList();
            settings.SettingsSchemaVersion = 903;
        }

        if (settings.SettingsSchemaVersion < 904)
        {
            // v0.9.4 adds adaptive Practice card density. Five cards per row
            // gives the requested default capacity of ten drivers in two rows.
            settings.PracticeCardsPerRow = 5;
            settings.SettingsSchemaVersion = 904;
        }

        if (settings.SettingsSchemaVersion < 906)
        {
            // v0.9.6 adds configurable current-heat movement colors to the
            // qualifying-round live position projection.
            var defaults = ThemeService.GetDashboardDefaults(settings.Theme);
            settings.DashboardPositionUpColor = defaults.PositionUp;
            settings.DashboardPositionDownColor = defaults.PositionDown;
            settings.DashboardPositionSameColor = defaults.PositionSame;
            settings.SettingsSchemaVersion = 906;
        }

        if (settings.SettingsSchemaVersion < 1000)
        {
            // v0.10 adds timed/lapped race formats and per-main bump-up rules.
            foreach (var cls in settings.Classes)
            {
                if (cls.TargetLaps <= 0)
                    cls.TargetLaps = 50;

                if (cls.MaxRacersPerMain is < 1 or > 30)
                    cls.MaxRacersPerMain = cls.MaxRacersPerHeat is >= 1 and <= 30
                        ? cls.MaxRacersPerHeat
                        : 19;

                cls.MainRules ??= [];
                if (cls.MainRules.Count == 0)
                {
                    cls.MainRules.Add(new MainRuleDefinition
                    {
                        Order = 1,
                        MaxRacers = cls.MaxRacersPerMain,
                        BumpInsFromLower = 0
                    });
                }
            }

            settings.SettingsSchemaVersion = 1000;
        }

        if (settings.SettingsSchemaVersion < 1001)
        {
            // v0.10.1 raises the individual race/heat/main capacity while
            // leaving class/event rosters themselves unrestricted.
            settings.SettingsSchemaVersion = 1001;
        }

        if (settings.SettingsSchemaVersion < 1100)
        {
            // v0.11 adds repeatable main legs (e.g. triple A mains),
            // best-N scoring, and configurable main seeding/sort.
            foreach (var cls in settings.Classes)
            {
                cls.MainRules ??= [];
                foreach (var rule in cls.MainRules)
                {
                    if (rule.RunCount <= 0)
                        rule.RunCount = 1;
                    if (rule.CountBest <= 0 || rule.CountBest > rule.RunCount)
                        rule.CountBest = 1;
                }
            }

            settings.SettingsSchemaVersion = 1100;
        }


        if (settings.SettingsSchemaVersion < 1650)
        {
            if (settings.StartTone == TonePattern.StartChime && string.IsNullOrWhiteSpace(settings.StartToneFile))
            {
                settings.StartTone = TonePattern.LongBeep;
                settings.StartToneLength = ToneLength.Normal;
            }
            settings.SettingsSchemaVersion = 1650;
        }
        if (settings.SettingsSchemaVersion < 1660)
        {
            settings.RaceHalfwayToneEnabled = true;
            settings.RaceTwoMinutesEnabled = true;
            settings.RaceOneMinuteEnabled = true;
            settings.SettingsSchemaVersion = 1660;
        }
        if (settings.SettingsSchemaVersion < 1680)
        {
            if (settings.StartTone is TonePattern.LongBeep or TonePattern.StartChime && string.IsNullOrWhiteSpace(settings.StartToneFile))
                settings.StartTone = TonePattern.AirHorn;
            settings.SettingsSchemaVersion = 1680;
        }
        if (!Enum.IsDefined(settings.FirstCrossingTone)) settings.FirstCrossingTone = TonePattern.DoublePulse;
        if (!Enum.IsDefined(settings.DriverFinishTone)) settings.DriverFinishTone = TonePattern.DriverFinish;
        if (!Enum.IsDefined(settings.HalfwayTone)) settings.HalfwayTone = TonePattern.Silent;
        if (!Enum.IsDefined(settings.FirstCrossingToneLength)) settings.FirstCrossingToneLength = ToneLength.Normal;
        if (!Enum.IsDefined(settings.DriverFinishToneLength)) settings.DriverFinishToneLength = ToneLength.Normal;
        if (!Enum.IsDefined(settings.HalfwayToneLength)) settings.HalfwayToneLength = ToneLength.Normal;

        settings.RaceLeaderCount = Math.Clamp(settings.RaceLeaderCount, 1, 5);
        settings.RaceAnnouncementRate = Math.Clamp(settings.RaceAnnouncementRate, -10, 10);
        settings.RaceCountdownSeconds = Math.Clamp(settings.RaceCountdownSeconds, 1, 30);
        settings.RaceLeaderIntervalSeconds = Math.Clamp(settings.RaceLeaderIntervalSeconds, 0, 60);

        foreach (var series in settings.Series)
        {
            series.EventIds ??= [];
            if (string.IsNullOrWhiteSpace(series.PointsCsv))
                series.PointsCsv = RaceSeriesDefinition.DefaultPointsCsv;
            if (series.EndDate < series.StartDate)
                series.EndDate = series.StartDate;
        }

        if (settings.DashboardMinimumLapGlowSeconds < 0.1 ||
            settings.DashboardMinimumLapGlowSeconds > 30)
        {
            settings.DashboardMinimumLapGlowSeconds = 1.0;
        }

        if (!Enum.IsDefined(settings.Theme))
            settings.Theme = AppTheme.Dark;

        var movementDefaults = ThemeService.GetDashboardDefaults(settings.Theme);
        if (!ThemeService.IsValidColor(settings.DashboardPositionUpColor))
            settings.DashboardPositionUpColor = movementDefaults.PositionUp;
        if (!ThemeService.IsValidColor(settings.DashboardPositionDownColor))
            settings.DashboardPositionDownColor = movementDefaults.PositionDown;
        if (!ThemeService.IsValidColor(settings.DashboardPositionSameColor))
            settings.DashboardPositionSameColor = movementDefaults.PositionSame;

        if (!Enum.IsDefined(settings.StartTone))
            settings.StartTone = TonePattern.StartChime;
        if (!Enum.IsDefined(settings.MinimumLapTone))
            settings.MinimumLapTone = TonePattern.WarningTriple;
        if (!Enum.IsDefined(settings.EndTone))
            settings.EndTone = TonePattern.FinishDrop;
        if (!Enum.IsDefined(settings.PassingTone))
            settings.PassingTone = TonePattern.PassingTick;

        if (!Enum.IsDefined(settings.StartToneLength))
            settings.StartToneLength = ToneLength.Normal;
        if (!Enum.IsDefined(settings.MinimumLapToneLength))
            settings.MinimumLapToneLength = ToneLength.Normal;
        if (!Enum.IsDefined(settings.EndToneLength))
            settings.EndToneLength = ToneLength.Normal;
        if (!Enum.IsDefined(settings.PassingToneLength))
            settings.PassingToneLength = ToneLength.Short;

        settings.StartToneFile ??= string.Empty;
        settings.MinimumLapToneFile ??= string.Empty;
        settings.EndToneFile ??= string.Empty;
        settings.PassingToneFile ??= string.Empty;

        settings.DashboardVisibleColumns ??= [];
        settings.DashboardColumnOrder ??= [];
        settings.PracticeVisibleStats ??= [];

        var validPracticeStats = PracticeStatCatalog.All
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        settings.PracticeVisibleStats = settings.PracticeVisibleStats
            .Where(validPracticeStats.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (settings.PracticeCardsPerRow is < 4 or > 6)
            settings.PracticeCardsPerRow = 5;

        var optionalDashboardColumns = DashboardColumnCatalog.OptionalColumns
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        settings.DashboardVisibleColumns = settings.DashboardVisibleColumns
            .Where(optionalDashboardColumns.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        settings.DashboardColumnOrder = settings.DashboardColumnOrder
            .Where(optionalDashboardColumns.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var id in DashboardColumnCatalog.OptionalColumns)
        {
            if (!settings.DashboardColumnOrder.Contains(id, StringComparer.OrdinalIgnoreCase))
                settings.DashboardColumnOrder.Add(id);
        }

        // An empty visibility collection is a valid user choice. For settings
        // created before v0.8.6, the schema migration above already populated it.
        var dashboardDefaults = ThemeService.GetDashboardDefaults(settings.Theme);

        settings.DashboardBackgroundColor =
            ThemeService.IsValidColor(settings.DashboardBackgroundColor)
                ? settings.DashboardBackgroundColor.Trim()
                : dashboardDefaults.Background;
        settings.DashboardPanelColor =
            ThemeService.IsValidColor(settings.DashboardPanelColor)
                ? settings.DashboardPanelColor.Trim()
                : dashboardDefaults.Panel;
        settings.DashboardRowColor =
            ThemeService.IsValidColor(settings.DashboardRowColor)
                ? settings.DashboardRowColor.Trim()
                : dashboardDefaults.Row;
        settings.DashboardAlternateRowColor =
            ThemeService.IsValidColor(settings.DashboardAlternateRowColor)
                ? settings.DashboardAlternateRowColor.Trim()
                : dashboardDefaults.AlternateRow;
        settings.DashboardTextColor =
            ThemeService.IsValidColor(settings.DashboardTextColor)
                ? settings.DashboardTextColor.Trim()
                : dashboardDefaults.Text;
        settings.DashboardPassingHighlightColor =
            ThemeService.IsValidColor(settings.DashboardPassingHighlightColor)
                ? settings.DashboardPassingHighlightColor.Trim()
                : dashboardDefaults.PassingHighlight;
        settings.DashboardFastestLapHighlightColor =
            ThemeService.IsValidColor(settings.DashboardFastestLapHighlightColor)
                ? settings.DashboardFastestLapHighlightColor.Trim()
                : dashboardDefaults.FastestLapHighlight;
        settings.DashboardNewBestLapHighlightColor =
            ThemeService.IsValidColor(settings.DashboardNewBestLapHighlightColor)
                ? settings.DashboardNewBestLapHighlightColor.Trim()
                : dashboardDefaults.NewBestLapHighlight;
        settings.DashboardCheckInGlowColor =
            ThemeService.IsValidColor(settings.DashboardCheckInGlowColor)
                ? settings.DashboardCheckInGlowColor.Trim()
                : dashboardDefaults.CheckInGlow;

        if (settings.DashboardColorsFollowTheme)
        {
            settings.DashboardBackgroundColor = dashboardDefaults.Background;
            settings.DashboardPanelColor = dashboardDefaults.Panel;
            settings.DashboardRowColor = dashboardDefaults.Row;
            settings.DashboardAlternateRowColor = dashboardDefaults.AlternateRow;
            settings.DashboardTextColor = dashboardDefaults.Text;
            settings.DashboardPassingHighlightColor = dashboardDefaults.PassingHighlight;
            settings.DashboardFastestLapHighlightColor = dashboardDefaults.FastestLapHighlight;
            settings.DashboardNewBestLapHighlightColor = dashboardDefaults.NewBestLapHighlight;
            settings.DashboardCheckInGlowColor = dashboardDefaults.CheckInGlow;
        }

        // Remove dangling assignments but keep valid data from previous runs.
        var driverIds = settings.Drivers.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var classIds = settings.Classes.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        settings.DriverClassAssignments = settings.DriverClassAssignments
            .Where(a => driverIds.Contains(a.DriverId) && classIds.Contains(a.ClassId))
            .GroupBy(a => a.Id)
            .Select(g => g.First())
            .ToList();

        foreach (var ev in settings.Events)
        {
            ev.ClassIds ??= [];
            ev.DriverAssignments ??= [];
            ev.Sessions ??= [];
            ev.Rankings ??= [];
            ev.ClassIds = ev.ClassIds.Where(classIds.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            ev.DriverAssignments = ev.DriverAssignments
                .Where(a => driverIds.Contains(a.DriverId) && ev.ClassIds.Contains(a.ClassId))
                .GroupBy(a => $"{a.DriverId}\u001F{a.ClassId}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var classOrder = ev.ClassIds
                .Select((id, index) => new { id, index })
                .ToDictionary(x => x.id, x => x.index, StringComparer.OrdinalIgnoreCase);

            var orderedSessions = ev.Sessions
                .OrderBy(s => s.SessionType == SessionType.Qualifying ? 0 : s.SessionType == SessionType.Race ? 1 : 2)
                .ThenBy(s => s.SessionType == SessionType.Race ? -s.Heat : s.Round)
                .ThenBy(s => classOrder.TryGetValue(s.ClassId, out var index) ? index : int.MaxValue)
                .ThenBy(s => s.SessionType == SessionType.Race ? s.MainLeg : s.Heat)
                .ToList();

            for (var i = 0; i < orderedSessions.Count; i++)
            {
                var session = orderedSessions[i];
                session.Entrants ??= [];
                session.Results ??= [];
                session.Laps ??= [];
                session.Breakouts ??= [];

                if (!Enum.IsDefined(session.QualificationOrder))
                    session.QualificationOrder = QualificationOrder.Random;
                if (!Enum.IsDefined(session.MainSortOrder))
                    session.MainSortOrder = MainSortOrder.QualifyingRank;
                if (session.SessionType == SessionType.Race)
                {
                    session.MainLeg = Math.Max(1, session.MainLeg);
                    session.MainRunCount = Math.Max(1, session.MainRunCount);
                    session.MainCountBest = Math.Clamp(
                        session.MainCountBest,
                        1,
                        session.MainRunCount);
                }
                if (!Enum.IsDefined(session.ProgressStatus))
                    session.ProgressStatus = EventSessionStatus.Planned;
                if (session.QueueOrder <= 0)
                    session.QueueOrder = i + 1;
            }
            RaceSchedule.AssignRaceNumbers(ev.Sessions);
        }

        if (string.IsNullOrWhiteSpace(settings.LastClassId) || settings.Classes.All(c => c.Id != settings.LastClassId))
            settings.LastClassId = settings.Classes[0].Id;

        if (settings.Events.Count == 0)
        {
            settings.LastEventId = string.Empty;
        }
        else if (string.IsNullOrWhiteSpace(settings.LastEventId) || settings.Events.All(e => e.Id != settings.LastEventId))
        {
            settings.LastEventId = settings.Events[0].Id;
        }
    }

    private static void RemoveGeneratedPlaceholderDrivers(AppSettings settings)
    {
        // Only remove unmistakable auto-generated placeholders from v0.3.0:
        // exact "Car N" name + nickname, N=1..19, with no real transponder data.
        var placeholderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var driver in settings.Drivers)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                driver.Name ?? string.Empty,
                @"^Car (?:[1-9]|1[0-9])$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!match.Success ||
                !string.Equals(driver.Name, driver.Nickname, StringComparison.OrdinalIgnoreCase))
                continue;

            var assignments = settings.DriverClassAssignments
                .Where(a => string.Equals(a.DriverId, driver.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // If a placeholder was given a transponder, treat it as user data and preserve it.
            if (assignments.Any(a => !string.IsNullOrWhiteSpace(a.TransponderId)))
                continue;

            placeholderIds.Add(driver.Id);
        }

        if (placeholderIds.Count == 0)
            return;

        settings.Drivers = settings.Drivers
            .Where(d => !placeholderIds.Contains(d.Id))
            .ToList();

        settings.DriverClassAssignments = settings.DriverClassAssignments
            .Where(a => !placeholderIds.Contains(a.DriverId))
            .ToList();

        // Legacy 1-19 slots are no longer used as the driver directory.
        settings.Racers = [];
    }
}
