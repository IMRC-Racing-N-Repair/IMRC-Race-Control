namespace IMRCRaceControl.Models;

public sealed class AppSettings
{
    public OnlinePublishingSettings OnlinePublishing { get; set; } = new();
    public TrackProfile Track { get; set; } = new();
    public string DriverCrossingStyle { get; set; } = "Box";
    public string DriverCrossingColor { get; set; } = "#35C76F";
    public List<string>? SpectatorVisibleColumns { get; set; }
    public bool SpectatorShowHeader { get; set; } = true;
    public bool SpectatorShowClock { get; set; } = true;
    public bool SpectatorShowNextUp { get; set; } = true;
    public bool MainZoomHeader { get; set; }
    public bool PopoutZoomHeader { get; set; }
    public double MainScreenZoom { get; set; } = 100;
    public double PopoutScreenZoom { get; set; } = 100;
    public double OverallPositionZoom { get; set; } = 100;
    public bool WebShowOverallPosition { get; set; } = true;
    public string FinishedDriverStyle { get; set; } = "Row";
    public string FinishedDriverColor { get; set; } = "#808080";
    public int SettingsSchemaVersion { get; set; } = 0;

    // Legacy/ad-hoc session fields are kept so v0.2 settings migrate cleanly.
    public string EventName { get; set; } = "Club Race";
    public string ClassName { get; set; } = "Open";
    public double RaceMinutes { get; set; } = 5;
    public double MinimumLapSeconds { get; set; } = 1.0;
    public bool PracticeMode { get; set; }
    public List<RacerEntry> Racers { get; set; } = [];

    public List<ClassDefinition> Classes { get; set; } = [];
    public List<DriverProfile> Drivers { get; set; } = [];
    public List<DriverClassAssignment> DriverClassAssignments { get; set; } = [];
    public List<RaceDayEventDefinition> Events { get; set; } = [];
    public DriverRatingSettings DriverRatingSettings { get; set; } = new();
    public List<DriverRatingReview> DriverRatingReviews { get; set; } = [];
    public List<RaceSeriesDefinition> Series { get; set; } = [];
    public string LastEventId { get; set; } = string.Empty;
    public string LastSeriesId { get; set; } = string.Empty;
    public string LastClassId { get; set; } = string.Empty;
    public SessionType LastSessionType { get; set; } = SessionType.Race;

    public string LastConnectionType { get; set; } = "USB / Serial (COM)";
    public string LastComPort { get; set; } = string.Empty;
    public int LastBaudRate { get; set; } = 115200;
    public string LastDataBits { get; set; } = "8";
    public string LastParity { get; set; } = "None";
    public string LastStopBits { get; set; } = "1";
    public string LastHandshake { get; set; } = "None";
    public string LastHost { get; set; } = "192.168.1.50";
    public int LastTcpPort { get; set; } = 5403;
    public string OpenStintHost { get; set; } = "127.0.0.1";
    public int OpenStintPort { get; set; } = 5556;
    public bool AutoReconnect { get; set; } = true;

    public bool LapAnnouncementsEnabled { get; set; }
    // Older installs inherit their existing lap switch until practice is set separately.
    public bool? PracticeLapAnnouncementsEnabled { get; set; }
    public bool PracticeLapCalloutsEnabled => PracticeLapAnnouncementsEnabled ?? LapAnnouncementsEnabled;
    public string LapSpeechVoice { get; set; } = string.Empty;
    public bool LapAnnouncementsTenthsOnly { get; set; }
    public bool AnnounceCheckInNames { get; set; } = true;
    public int LapSpeechRate { get; set; } = 2;
    public int NameSpeechRate { get; set; } = 0;
    public LapSpeechBusyMode LapSpeechBusyMode { get; set; } = global::IMRCRaceControl.Models.LapSpeechBusyMode.LatestOnly;
    public int LapSpeechMaxAgeMilliseconds { get; set; } = 500;
    public int LapSpeechVolume { get; set; } = 100;

    public bool RaceLeadersEnabled { get; set; }
    public int RaceLeaderCount { get; set; } = 3;
    public int RaceLeaderIntervalSeconds { get; set; } = 30;
    public int RaceAnnouncementRate { get; set; } = 0;
    public bool RaceHalfwayToneEnabled { get; set; } = true;
    public bool RaceCountdownEnabled { get; set; }
    public int RaceCountdownSeconds { get; set; } = 10;
    public bool RaceTwoMinutesEnabled { get; set; } = true;
    public bool RaceOneMinuteEnabled { get; set; } = true;
    public bool RaceFirstCrossingToneEnabled { get; set; } = true;
    public bool RaceDriverFinishToneEnabled { get; set; } = true;

    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public TonePattern StartTone { get; set; } = TonePattern.AirHorn;
    public ToneLength StartToneLength { get; set; } = ToneLength.Normal;

    public TonePattern FirstCrossingTone { get; set; } = TonePattern.DoublePulse;
    public ToneLength FirstCrossingToneLength { get; set; } = ToneLength.Normal;
    public string FirstCrossingToneFile { get; set; } = string.Empty;
    public TonePattern DriverFinishTone { get; set; } = TonePattern.DriverFinish;
    public ToneLength DriverFinishToneLength { get; set; } = ToneLength.Normal;
    public string DriverFinishToneFile { get; set; } = string.Empty;
    public TonePattern HalfwayTone { get; set; } = TonePattern.Silent;
    public ToneLength HalfwayToneLength { get; set; } = ToneLength.Normal;
    public string HalfwayToneFile { get; set; } = string.Empty;

    public TonePattern MinimumLapTone { get; set; } = TonePattern.WarningTriple;
    public ToneLength MinimumLapToneLength { get; set; } = ToneLength.Normal;

    public TonePattern EndTone { get; set; } = TonePattern.FinishDrop;
    public ToneLength EndToneLength { get; set; } = ToneLength.Normal;

    public TonePattern PassingTone { get; set; } = TonePattern.PassingTick;
    public ToneLength PassingToneLength { get; set; } = ToneLength.Short;

    public string StartToneFile { get; set; } = string.Empty;
    public string MinimumLapToneFile { get; set; } = string.Empty;
    public string EndToneFile { get; set; } = string.Empty;
    public string PassingToneFile { get; set; } = string.Empty;

    public List<string> DashboardVisibleColumns { get; set; } = DashboardColumnCatalog.OptionalColumns.ToList();
    public List<string> DashboardColumnOrder { get; set; } = DashboardColumnCatalog.OptionalColumns.ToList();

    // When true, dashboard colors automatically change with Light/Dark/Blue theme.
    public bool DashboardColorsFollowTheme { get; set; } = true;

    public string DashboardBackgroundColor { get; set; } = string.Empty;
    public string DashboardPanelColor { get; set; } = string.Empty;
    public string DashboardRowColor { get; set; } = string.Empty;
    public bool DashboardAlternateRowsEnabled { get; set; } = true;
    public string DashboardAlternateRowColor { get; set; } = string.Empty;
    public string DashboardTextColor { get; set; } = string.Empty;
    public string DashboardPassingHighlightColor { get; set; } = string.Empty;
    public string DashboardFastestLapHighlightColor { get; set; } = string.Empty;
    public string DashboardNewBestLapHighlightColor { get; set; } = string.Empty;
    public string DashboardCheckInGlowColor { get; set; } = string.Empty;
    public string DashboardMinimumLapGlowColor { get; set; } = string.Empty;
    public double DashboardMinimumLapGlowSeconds { get; set; } = 1.0;

    public string DashboardPositionUpColor { get; set; } = string.Empty;
    public string DashboardPositionDownColor { get; set; } = string.Empty;
    public string DashboardPositionSameColor { get; set; } = string.Empty;

    public List<string> PracticeVisibleStats { get; set; } = PracticeStatCatalog.Defaults.ToList();
    public int PracticeCardsPerRow { get; set; } = 5;

}
