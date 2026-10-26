using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Media;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;
using IMRCRaceControl.Transport;

namespace IMRCRaceControl;

public partial class MainWindow : Window
{
    private readonly RaceEngine _raceEngine = new();
    private readonly SettingsService _settingsService;
    private readonly RaceControlDatabaseService _database;
    private readonly string? _startupWarning;
    private readonly RaceResultService _resultService = new();
    private readonly EventArchiveService _eventArchiveService = new();
    private readonly ConnectionController _connection = new();
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private readonly TrafficLogService _trafficLogger = new();
    private readonly DecoderIngressService _decoderIngress = new();
    private readonly DispatcherTimer _raceTimer;
    private readonly ObservableCollection<DriverClassAssignment> _driverAssignments = new();

    private AppSettings _settings;
    private ClassDefinition? _editingClass;
    private DriverProfile? _editingDriver;
    private RaceDayEventDefinition? _activeEvent;
    private EventSessionDefinition? _loadedSession;
    private long _chunkCount;
    private long _byteCount;
    private bool _closing;
    private bool _resultAutoSaved;
    private bool _resultAutoSaveInProgress;
    private bool _suppressUiEvents;
    private bool _recallMode;
    private RaceState _lastRenderedRaceState = RaceState.Ready;

    private Point _heatDragStartPoint;
    private SessionEntrant? _draggedHeatEntrant;
    private EventSessionDefinition? _dragSourceHeat;
    private Point _sessionDragStartPoint;
    private EventSessionDefinition? _draggedSession;
    private const string HeatEntrantDragFormat = "IMRCRaceControl.SessionEntrant";
    private const string HeatSessionDragFormat = "IMRCRaceControl.EventSession";

    public ObservableCollection<ClassDefinition> Classes { get; } = new();
    public ObservableCollection<DriverProfile> Drivers { get; } = new();
    public ObservableCollection<AssignmentViewRow> AssignmentRows { get; } = new();
    public ObservableCollection<RaceDayEventDefinition> Events { get; } = new();
    public ObservableCollection<EventClassChoice> EventClassChoices { get; } = new();
    public ObservableCollection<EventClassRow> EventSelectedClasses { get; } = new();
    public ObservableCollection<EventDriverChoice> EventDriverChoices { get; } = new();
    public ObservableCollection<EventRosterRow> EventRosterRows { get; } = new();
    public ObservableCollection<DriverProfile> AvailableEventDrivers { get; } = new();
    public ObservableCollection<EventSessionDefinition> PlannedSessions { get; } = new();
    public ObservableCollection<EventSessionDefinition> RaceDashboardSessions { get; } = new();
    public ObservableCollection<SessionEntrant> SelectedHeatEntrants { get; } = new();
    public ObservableCollection<EventRankingEntry> EventRankings { get; } = new();
    public ObservableCollection<RacerEntry> SessionRacers { get; } = new();
    public ObservableCollection<StandingRow> Standings { get; } = new();
    public ObservableCollection<RoundProjectionRow> RoundProjectionRows { get; } = new();
    public ObservableCollection<DriverLapViewRow> DriverLapRows { get; } = new();
    public ObservableCollection<LapRecord> RecentLaps { get; } = new();
    public ObservableCollection<TrafficRow> Traffic { get; } = new();

    public MainWindow(AppSettings settings, SettingsService settingsService,
        RaceControlDatabaseService database, string? startupWarning = null)
    {
        _settings = settings;
        _settingsService = settingsService;
        _database = database;
        _startupWarning = startupWarning;
        InitializeComponent();
        DataContext = this;
        InitializePracticeSeriesInfrastructure();

        _raceTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _raceTimer.Tick += RaceTimer_Tick;

        _connection.BytesReceived += Connection_BytesReceived;
        _connection.StatusChanged += Connection_StatusChanged;
        _decoderIngress.PacketFormatted += DecoderIngress_PacketFormatted;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_startupWarning is not null)
            MessageBox.Show(this, _startupWarning, "Database startup warning",
                MessageBoxButton.OK, MessageBoxImage.Warning);

        ApplyLapSpeechControls();
        ThemeService.ApplyDashboard(_settings);
        ApplyDashboardPreferences();
        ApplyPracticeLayoutPreferences();
        LoadSettingsIntoCollections();
        RefreshRentalContext();
        LoadSeriesFromSettings();
        LoadTrackInfo();
        InitializeOnlinePublishing();
        MainNextRaceStrip.PreviewProvider = GetNextRacePreview;

        try
        {
            await InitializePracticeDatabaseAsync();
        }
        catch (Exception ex)
        {
            AppCrashLogger.Log("Practice database initialization", ex);
            FooterStatusText.Text = $"Practice database warning: {ex.Message}";
        }

        ApplyConnectionSettings();
        RefreshPorts();

        _suppressUiEvents = true;
        try
        {
            RaceClassCombo.SelectedItem = Classes.FirstOrDefault(c => c.Id == _settings.LastClassId) ?? Classes.FirstOrDefault();
            SelectComboByText(SessionTypeCombo, _settings.LastSessionType.ToString());

            var selectedEvent = Events.FirstOrDefault(e => e.Id == _settings.LastEventId) ?? Events.FirstOrDefault();
            EventSelectCombo.SelectedItem = selectedEvent;
            _activeEvent = selectedEvent;
        }
        finally
        {
            _suppressUiEvents = false;
        }

        LoadEventToEditor(_activeEvent);
        UpdateRaceDashboardAvailability();
        RebuildSessionQueueOrder();
        RefreshRaceDayFlow();

        if (_activeEvent is null)
        {
            MainTabs.SelectedItem = EventTab;
            HeaderMessageText.Text = "Create a race-day event to unlock the Race Dashboard.";
        }
        else
        {
            UpdateRaceDefaultsFromClass();
            HeaderMessageText.Text = $"Event ready: {_activeEvent.Name}";
        }

        RefreshRaceUi(DateTimeOffset.Now);
        FooterStatusText.Text = _activeEvent is null
            ? "Create a race-day event first. Then add classes, drivers, and build rounds or mains."
            : "Event ready. Build rounds/mains in Event Setup or run the current heat.";

        if (_activeEvent is not null && MainTabs.SelectedItem == RaceTab)
            EnsureCurrentSessionLoadedForDashboard();
    }

    private void LoadSettingsIntoCollections()
    {
        Classes.Clear();
        foreach (var item in _settings.Classes)
            Classes.Add(item);

        Drivers.Clear();
        foreach (var item in _settings.Drivers)
            Drivers.Add(item);

        _driverAssignments.Clear();
        foreach (var item in _settings.DriverClassAssignments)
            _driverAssignments.Add(item);

        Events.Clear();
        foreach (var item in _settings.Events)
            Events.Add(item);

        RefreshAssignmentRows();
    }

    private void ApplyConnectionSettings()
    {
        ApplyScreenZoom();
        AutoReconnectCheck.IsChecked = true;
        HostText.Text = _settings.LastHost;
        TcpPortText.Text = _settings.LastTcpPort.ToString();
        OpenStintHostText.Text = _settings.OpenStintHost;
        OpenStintPortText.Text = _settings.OpenStintPort.ToString();
        SelectComboByText(ConnectionTypeCombo, _settings.LastConnectionType);
        BaudCombo.Text = _settings.LastBaudRate.ToString();
        SelectComboByText(DataBitsCombo, _settings.LastDataBits);
        SelectComboByText(ParityCombo, _settings.LastParity);
        SelectComboByText(StopBitsCombo, _settings.LastStopBits);
        SelectComboByText(HandshakeCombo, _settings.LastHandshake);
        UpdateConnectionPanelVisibility();
    }

    private async Task CaptureAndSaveSettingsAsync(bool reportFailureToCaller = false)
    {
        await _settingsSaveGate.WaitAsync();
        try
        {
            _settings.Classes = Classes.ToList();
            _settings.Drivers = Drivers.ToList();
            _settings.DriverClassAssignments = _driverAssignments.ToList();
            _settings.Events = Events.ToList();
            _settings.Series = SeriesDefinitions.ToList();
            _settings.LastSeriesId = _activeSeries?.Id ?? string.Empty;
            _settings.LastClassId = (RaceClassCombo.SelectedItem as ClassDefinition)?.Id ?? _settings.LastClassId;
            _settings.LastEventId = _activeEvent?.Id ?? string.Empty;
            _settings.LastSessionType = GetSelectedSessionType();
            _settings.EventName = RaceEventText.Text.Trim();
            _settings.ClassName = (RaceClassCombo.SelectedItem as ClassDefinition)?.Name ?? _settings.ClassName;

            if (double.TryParse(RaceMinutesText.Text, out var minutes) && minutes > 0)
                _settings.RaceMinutes = minutes;
            if (double.TryParse(MinimumLapText.Text, out var minLap) && minLap >= 0)
                _settings.MinimumLapSeconds = minLap;

            _settings.AutoReconnect = true;
            _settings.LastConnectionType = SelectedText(ConnectionTypeCombo);
            _settings.LastComPort = PortCombo.SelectedItem?.ToString() ?? _settings.LastComPort;
            if (int.TryParse(EditableComboText(BaudCombo), out var baud) && baud > 0)
                _settings.LastBaudRate = baud;
            _settings.LastDataBits = SelectedText(DataBitsCombo);
            _settings.LastParity = SelectedText(ParityCombo);
            _settings.LastStopBits = SelectedText(StopBitsCombo);
            _settings.LastHandshake = SelectedText(HandshakeCombo);
            _settings.OpenStintHost = OpenStintHostText.Text.Trim();
            if (int.TryParse(OpenStintPortText.Text, out var openStintPort) && openStintPort is >= 1 and <= 65535)
                _settings.OpenStintPort = openStintPort;
            _settings.LastHost = HostText.Text.Trim();
            if (int.TryParse(TcpPortText.Text, out var port) && port is >= 1 and <= 65535)
                _settings.LastTcpPort = port;

            try
            {
                // SQLite is authoritative. This saves the complete application
                // snapshot and synchronizes the queryable racing tables atomically.
                await _database.SaveApplicationStateAsync(_settings);

                // Keep settings.json as a readable migration/disaster-recovery backup.
                try { await _settingsService.SaveAsync(_settings); }
                catch (Exception backupError)
                {
                    AppCrashLogger.Log("Settings backup save", backupError);
                    FooterStatusText.Text = "Saved to database; settings backup could not be updated.";
                }
            }
            catch (Exception ex)
            {
                AppCrashLogger.Log("Application state save", ex);
                FooterStatusText.Text =
                    $"Database save warning: {ex.Message} • details: {AppCrashLogger.LogDirectory}";
                if (reportFailureToCaller) throw;
            }
        }
        finally
        {
            _settingsSaveGate.Release();
        }
    }

    private async Task SaveSettingsSafelyAsync()
    {
        try
        {
            await CaptureAndSaveSettingsAsync();
        }
        catch (Exception ex)
        {
            FooterStatusText.Text = $"Settings save warning: {ex.Message}";
        }
    }

    // -----------------------------
    // Class setup
    // -----------------------------

    private void ClassGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _editingClass = ClassGrid.SelectedItem as ClassDefinition;
        UpdateClassDetail(_editingClass);
    }

    private async void NewClass_Click(object sender, RoutedEventArgs e)
    {
        var item = ShowClassEditor();
        if (item is null)
            return;

        _editingClass = item;
        ClassGrid.SelectedItem = item;
        if (RaceClassCombo.SelectedItem is null)
            RaceClassCombo.SelectedItem = item;
        UpdateRaceDefaultsFromClass();
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"Class '{item.Name}' created.";
    }

    private async void EditClass_Click(object sender, RoutedEventArgs e)
    {
        var selected = ClassGrid.SelectedItem as ClassDefinition ?? _editingClass;
        if (selected is null)
        {
            FooterStatusText.Text = "Select a class to edit.";
            return;
        }

        var item = ShowClassEditor(selected);
        if (item is null)
            return;

        _editingClass = item;
        ClassGrid.SelectedItem = item;
        ClassGrid.Items.Refresh();
        RefreshEventClassRows(item.Id);
        RefreshPlannedSessions();
        UpdateRaceDefaultsFromClass();
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"Class '{item.Name}' updated.";
    }

    private async void DeleteClass_Click(object sender, RoutedEventArgs e)
    {
        if (ClassGrid.SelectedItem is not ClassDefinition selected)
            return;
        if (Classes.Count <= 1)
        {
            MessageBox.Show(this, "At least one class must remain.", "Class setup", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"Delete class '{selected.Name}' and its driver assignments/planned sessions?", "Delete class",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        foreach (var assignment in _driverAssignments.Where(a => a.ClassId == selected.Id).ToList())
            _driverAssignments.Remove(assignment);
        foreach (var ev in Events)
        {
            ev.ClassIds.RemoveAll(id => id == selected.Id);
            ev.DriverAssignments.RemoveAll(a => a.ClassId == selected.Id);
            ev.Sessions.RemoveAll(s => s.ClassId == selected.Id);
        }
        Classes.Remove(selected);
        _editingClass = null;
        RaceClassCombo.SelectedItem = Classes.FirstOrDefault();
        UpdateClassDetail(null);
        RefreshAssignmentRows();
        RefreshEventClassChoices();
        RefreshPlannedSessions();
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"Class '{selected.Name}' deleted.";
    }

    // -----------------------------
    // Drivers and class/transponder assignments
    // -----------------------------

    private void DriverGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriverGrid.SelectedItem is not DriverProfile selected)
        {
            _editingDriver = null;
            UpdateDriverProfileDetail(null);
            AssignmentPanel.Visibility = Visibility.Collapsed;
            AssignmentEmptyPrompt.Visibility = Visibility.Visible;
            AssignmentRows.Clear();
            AssignmentGrid.SelectedItem = null;
            return;
        }

        _editingDriver = selected;
        UpdateDriverProfileDetail(selected);

        AssignmentEmptyPrompt.Visibility = Visibility.Collapsed;
        AssignmentPanel.Visibility = Visibility.Visible;
        SelectedDriverAssignmentsTitleText.Text = $"{selected.DisplayName.ToUpperInvariant()} • CLASS / TRANSPONDER SETUP";
        ClearAssignmentEditor();
        RefreshAssignmentRows();
    }

    private async void NewDriver_Click(object sender, RoutedEventArgs e)
    {
        var driver = ShowDriverEditor();
        if (driver is null)
            return;

        _editingDriver = driver;
        DriverGrid.SelectedItem = driver;
        AssignmentEmptyPrompt.Visibility = Visibility.Collapsed;
        AssignmentPanel.Visibility = Visibility.Visible;
        SelectedDriverAssignmentsTitleText.Text = $"{driver.DisplayName.ToUpperInvariant()} • CLASS / TRANSPONDER SETUP";
        RefreshAssignmentRows();
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"Driver '{driver.DisplayName}' created.";
    }

    private async void EditDriver_Click(object sender, RoutedEventArgs e)
    {
        var driver = _editingDriver ?? DriverGrid.SelectedItem as DriverProfile;
        if (driver is null)
        {
            FooterStatusText.Text = "Select a driver to edit.";
            return;
        }

        if (ShowDriverEditor(driver) is null)
            return;

        DriverGrid.SelectedItem = driver;
        DriverGrid.Items.Refresh();
        SelectedDriverAssignmentsTitleText.Text = $"{driver.DisplayName.ToUpperInvariant()} • CLASS / TRANSPONDER SETUP";
        RefreshAssignmentRows();
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"Driver '{driver.DisplayName}' updated.";
    }

    private async void DeleteDriver_Click(object sender, RoutedEventArgs e)
    {
        var selected = _editingDriver ?? DriverGrid.SelectedItem as DriverProfile;
        if (selected is null)
            return;

        if (MessageBox.Show(this,
                $"Delete driver '{selected.DisplayName}' and all of their class/transponder assignments?",
                "Delete driver",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        foreach (var assignment in _driverAssignments.Where(a => a.DriverId == selected.Id).ToList())
            _driverAssignments.Remove(assignment);

        foreach (var ev in Events)
        {
            ev.DriverAssignments.RemoveAll(a => a.DriverId == selected.Id);
            foreach (var session in ev.Sessions)
                session.Entrants.RemoveAll(ent => ent.DriverId == selected.Id);
        }

        Drivers.Remove(selected);
        _editingDriver = null;
        DriverGrid.SelectedItem = null;
        UpdateDriverProfileDetail(null);
        AssignmentPanel.Visibility = Visibility.Collapsed;
        AssignmentEmptyPrompt.Visibility = Visibility.Visible;
        AssignmentRows.Clear();
        ClearAssignmentEditor();
        RefreshEventDriverChoices();
        RefreshPlannedSessions();

        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"Driver '{selected.DisplayName}' deleted.";
    }

    private void NewAssignment_Click(object sender, RoutedEventArgs e)
    {
        if (_editingDriver is null)
        {
            FooterStatusText.Text = "Select a driver before adding a class assignment.";
            return;
        }

        ClearAssignmentEditor();
        AssignmentClassCombo.Focus();
    }

    private void AssignmentGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AssignmentGrid.SelectedItem is not AssignmentViewRow row)
        {
            AssignmentEditorTitleText.Text = "NEW CLASS ASSIGNMENT";
            return;
        }

        AssignmentEditorTitleText.Text = $"EDIT • {row.ClassName}";
        AssignmentClassCombo.SelectedItem = Classes.FirstOrDefault(c => c.Id == row.ClassId);
        AssignmentTransponderText.Text = row.TransponderId;
        AssignmentRankText.Text = row.SeedRank.ToString();
    }

    private async void AddAssignment_Click(object sender, RoutedEventArgs e)
    {
        if (_editingDriver is not DriverProfile driver)
        {
            MessageBox.Show(this, "Select a driver first.", "Assignment", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (AssignmentClassCombo.SelectedItem is not ClassDefinition cls)
        {
            MessageBox.Show(this, "Select the class this driver is running.", "Assignment", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(AssignmentRankText.Text, out var rank) || rank < 0)
        {
            MessageBox.Show(this, "Rank must be 0 or a positive whole number.", "Assignment", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var transponder = AssignmentTransponderText.Text.Trim();
        var selectedRow = AssignmentGrid.SelectedItem as AssignmentViewRow;
        var editingAssignment = selectedRow is null
            ? null
            : _driverAssignments.FirstOrDefault(a => a.Id == selectedRow.AssignmentId && a.DriverId == driver.Id);

        var duplicate = _driverAssignments.Any(a =>
            a.Id != editingAssignment?.Id &&
            a.DriverId == driver.Id &&
            a.ClassId == cls.Id &&
            string.Equals(a.TransponderId.Trim(), transponder, StringComparison.OrdinalIgnoreCase));

        if (duplicate)
        {
            MessageBox.Show(this,
                "That class/transponder assignment already exists for this driver.",
                "Assignment",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (editingAssignment is null)
        {
            _driverAssignments.Add(new DriverClassAssignment
            {
                DriverId = driver.Id,
                ClassId = cls.Id,
                TransponderId = transponder,
                SeedRank = rank
            });
        }
        else
        {
            editingAssignment.ClassId = cls.Id;
            editingAssignment.TransponderId = transponder;
            editingAssignment.SeedRank = rank;
        }

        RefreshAssignmentRows();
        ClearAssignmentEditor();
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"Saved {driver.DisplayName}'s {cls.Name} assignment.";
    }

    private async void RemoveAssignment_Click(object sender, RoutedEventArgs e)
    {
        if (_editingDriver is null || AssignmentGrid.SelectedItem is not AssignmentViewRow row)
        {
            FooterStatusText.Text = "Select one of this driver's assignments to remove it.";
            return;
        }

        var item = _driverAssignments.FirstOrDefault(a =>
            a.Id == row.AssignmentId && a.DriverId == _editingDriver.Id);
        if (item is null)
            return;

        if (MessageBox.Show(this,
                $"Remove {_editingDriver.DisplayName}'s {row.ClassName} assignment?",
                "Remove assignment",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _driverAssignments.Remove(item);
        RefreshAssignmentRows();
        ClearAssignmentEditor();
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"Removed {_editingDriver.DisplayName}'s {row.ClassName} assignment.";
    }

    private void ClearAssignmentEditor()
    {
        AssignmentGrid.SelectedItem = null;
        AssignmentEditorTitleText.Text = "NEW CLASS ASSIGNMENT";
        AssignmentClassCombo.SelectedItem = null;
        AssignmentTransponderText.Clear();
        AssignmentRankText.Text = "0";
    }

    private void RefreshAssignmentRows()
    {
        AssignmentRows.Clear();

        if (_editingDriver is null)
            return;

        var ratings = DriverRatingCalculator.Calculate(Events, Drivers, _settings.DriverRatingSettings, _settings.DriverRatingReviews)
            .Standings.Where(row => row.DriverId == _editingDriver.Id).ToDictionary(row => row.ClassId);
        var rows = _driverAssignments
            .Where(a => a.DriverId == _editingDriver.Id)
            .Select(a =>
            {
                var cls = Classes.FirstOrDefault(c => c.Id == a.ClassId);
                if (cls is null)
                    return null;

                return new AssignmentViewRow
                {
                    AssignmentId = a.Id,
                    DriverId = _editingDriver.Id,
                    Driver = _editingDriver.DisplayName,
                    ClassId = cls.Id,
                    ClassName = cls.Name,
                    TransponderId = a.TransponderId,
                    RatingDisplay = ratings.GetValueOrDefault(cls.Id)?.RatingDisplay ?? "1500",
                    SofDisplay = ratings.TryGetValue(cls.Id, out var rating) ? rating.LastStrengthOfField.ToString("0") : "—",
                    SafetyDisplay = ratings.GetValueOrDefault(cls.Id)?.SafetyDisplay ?? "3.00",
                    SeedRank = a.SeedRank
                };
            })
            .Where(r => r is not null)
            .Cast<AssignmentViewRow>()
            .OrderBy(r => r.ClassName)
            .ThenBy(r => r.SeedRank <= 0 ? int.MaxValue : r.SeedRank)
            .ToList();

        foreach (var row in rows)
            AssignmentRows.Add(row);

        AssignmentGrid.Items.Refresh();
    }

    // -----------------------------
    // Race day event / qualifier builder
    // -----------------------------

    private void EventSelectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_resultAutoSaveInProgress)
        {
            _suppressUiEvents = true;
            try { EventSelectCombo.SelectedItem = _activeEvent; }
            finally { _suppressUiEvents = false; }
            FooterStatusText.Text = "Wait for the completed race result to finish saving before switching events.";
            return;
        }

        if (_suppressUiEvents)
            return;

        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            MessageBox.Show(this, "End or reset the live session before switching events.", "Switch event",
                MessageBoxButton.OK, MessageBoxImage.Information);

            _suppressUiEvents = true;
            try { EventSelectCombo.SelectedItem = _activeEvent; }
            finally { _suppressUiEvents = false; }
            return;
        }

        _activeEvent = EventSelectCombo.SelectedItem as RaceDayEventDefinition;
        _loadedSession = null;
        _recallMode = false;
        _lapSpeech.Stop();
        _raceEngine.Reset();
        SessionRacers.Clear();
        Standings.Clear();
        RecentLaps.Clear();

        LoadEventToEditor(_activeEvent);
        UpdateRaceDashboardAvailability();
        RebuildSessionQueueOrder();
        RefreshRaceDayFlow();

        if (_activeEvent is null)
            MainTabs.SelectedItem = EventTab;
    }

    private void LoadEventToEditor(RaceDayEventDefinition? ev)
    {
        SelectedHeatEntrants.Clear();
        SelectedHeatTitleText.Text = "HEAT EDITOR";
        SelectedHeatInfoText.Text = "Select a qualification heat.";

        if (ev is null)
        {
            EventNameEditor.Text = string.Empty;
            EventDateTextBox.Text = DateTime.Today.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
            EventStatusText.Text = "—";
            RaceEventText.Text = "No Event";
            RefreshEventSetupCollections();
            RefreshPlannedSessions();
            UpdateEventEditorAvailability();
            return;
        }

        EventNameEditor.Text = ev.Name;
        EventDateTextBox.Text = ev.EventDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
        EventStatusText.Text = ev.StatusDisplay;
        RaceEventText.Text = ev.Name;

        var nextRound = ev.Sessions
            .Where(s => s.SessionType == SessionType.Qualifying)
            .Select(s => s.Round)
            .DefaultIfEmpty(0)
            .Max() + 1;
        QualRoundText.Text = Math.Max(1, nextRound).ToString();
        SelectComboByText(QualRoundOrderCombo, ev.QualificationOrder.ToDisplay());

        RefreshEventSetupCollections();
        RefreshPlannedSessions();
        UpdateEventEditorAvailability();
    }

    private void UpdateRaceDashboardAvailability()
    {
        var available = _activeEvent is not null;
        RaceTab.IsEnabled = available && !_practiceActive;

        if (!available)
        {
            RaceEventText.Text = "No Event";
            RaceSessionText.Text = "Create an event first";
            if (!_practiceActive)
            {
                HeaderRaceStateText.Text = "NO EVENT";
                HeaderMessageText.Text = "Create a race-day event to unlock the Race Dashboard.";
            }
        }
        else
        {
            RaceEventText.Text = _activeEvent!.Name;
            if (!_practiceActive)
            {
                HeaderMessageText.Text = _activeEvent.Status == EventStatus.Completed
                    ? $"Recalled completed event: {_activeEvent.Name}"
                    : $"Race day event: {_activeEvent.Name}";
            }
        }

        UpdateEventEditorAvailability();
        RefreshRaceDayQueue();
    }

    private void UpdateEventEditorAvailability()
    {
        var hasEvent = _activeEvent is not null;
        var editable = hasEvent && _activeEvent!.Status == EventStatus.Active;

        // Never disable the whole display panels. WPF propagates the disabled
        // visual state to every child and makes completed-event data difficult
        // to read. Completed events remain fully visible and browsable.
        EventSetupPanel.IsEnabled = true;
        EventClassesPanel.IsEnabled = true;
        EventRosterPanel.IsEnabled = true;

        // Only controls that can change event data are disabled.
        QualRoundBuilderPanel.IsEnabled = editable;
        EndEventButton.IsEnabled = editable;
        EventNewClassButton.IsEnabled = editable;
        EventClassUpButton.IsEnabled = editable;
        EventClassDownButton.IsEnabled = editable;
        EventNewDriverButton.IsEnabled = editable;
        RoundMainBuilderButton.IsEnabled = editable;

        // Browsing remains available for completed events.
        EventRosterClassCombo.IsEnabled = hasEvent && (_activeEvent?.ClassIds.Count ?? 0) > 0;
        EventDriverSearchText.IsEnabled = hasEvent;

        EventStatusText.Text = hasEvent ? _activeEvent!.StatusDisplay : "NO ACTIVE EVENT";

        // Rebuild row-level editability immediately when the event lifecycle changes.
        RefreshEventClassRows(_eventRosterClassId);
    }

    private void RefreshEventClassChoices()
    {
        RefreshEventClassRows(_eventRosterClassId);
    }

    private void RefreshPlannedSessions(string? selectSessionId = null)
    {
        var currentId = selectSessionId ?? (PlannedSessionGrid?.SelectedItem as EventSessionDefinition)?.Id;

        PlannedSessions.Clear();
        if (_activeEvent is not null)
        {
            RaceSchedule.AssignRaceNumbers(_activeEvent.Sessions);
            foreach (var session in RaceSchedule.DisplayOrder(_activeEvent.Sessions))
            {
                PlannedSessions.Add(session);
            }
        }

        ApplyEventSessionSort();
        PlannedSessionGrid?.Items.Refresh();

        if (!string.IsNullOrWhiteSpace(currentId))
        {
            var selected = PlannedSessions.FirstOrDefault(s => s.Id == currentId);
            if (selected is not null && PlannedSessionGrid is not null)
                PlannedSessionGrid.SelectedItem = selected;
        }

        if (PlannedSessionGrid?.SelectedItem is not EventSessionDefinition)
            LoadSelectedHeatEditor(null);

        RefreshRaceDayQueue();
    }

    private async void NewEvent_Click(object sender, RoutedEventArgs e)
    {
        var existingActiveEvent = Events.FirstOrDefault(ev => ev.Status == EventStatus.Active);
        if (existingActiveEvent is not null)
        {
            _activeEvent = existingActiveEvent;
            _suppressUiEvents = true;
            try { EventSelectCombo.SelectedItem = existingActiveEvent; }
            finally { _suppressUiEvents = false; }

            LoadEventToEditor(existingActiveEvent);
            UpdateRaceDashboardAvailability();

            MessageBox.Show(this,
                $"'{existingActiveEvent.Name}' is still the current active event. End the current event before creating another race day.",
                "Current event still active",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            MessageBox.Show(this,
                "Finish or reset the live race before creating another event.",
                "Race in progress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new Dialogs.EventEditorDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var ev = new RaceDayEventDefinition
        {
            Track = string.IsNullOrWhiteSpace(_settings.Track.Name) ? null : _settings.Track.Snapshot(),
            Name = dialog.EventName,
            EventDate = dialog.EventDate,
            CreatedAt = DateTimeOffset.Now,
            Status = EventStatus.Active,
            QualificationOrder = QualificationOrder.Random,
            ClassIds = []
        };

        Events.Add(ev);
        _activeEvent = ev;
        _recallMode = false;

        _suppressUiEvents = true;
        try { EventSelectCombo.SelectedItem = ev; }
        finally { _suppressUiEvents = false; }

        LoadEventToEditor(ev);
        UpdateRaceDashboardAvailability();
        RebuildSessionQueueOrder();
        RefreshRaceDayFlow();
        await CaptureAndSaveSettingsAsync();

        FooterStatusText.Text = $"Created '{ev.Name}'. Add classes and drivers, then build rounds or mains.";
    }

    private async void SaveEvent_Click(object sender, RoutedEventArgs e)
    {
        if (_activeEvent is null || _activeEvent.Status == EventStatus.Completed)
            return;

        SaveEventEditorToModel();
        RebuildSessionQueueOrder();
        EventSelectCombo.Items.Refresh();
        RaceEventText.Text = _activeEvent.Name;
        RefreshRaceDayFlow();
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"Event '{_activeEvent.Name}' saved.";
    }

    private void SaveEventEditorToModel()
    {
        if (_activeEvent?.Status == EventStatus.Completed || _activeEvent is null)
            return;

        var name = EventNameEditor.Text.Trim();
        _activeEvent.Name = string.IsNullOrWhiteSpace(name) ? "Race Day" : name;

        if (TryParseEventDate(EventDateTextBox.Text, out var eventDate))
            _activeEvent.EventDate = eventDate;
        else
            EventDateTextBox.Text = _activeEvent.EventDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);

        _activeEvent.ClassIds = EventSelectedClasses.Select(c => c.ClassId).ToList();
    }

    private async void DeleteEvent_Click(object sender, RoutedEventArgs e)
    {
        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            MessageBox.Show(this, "End or reset the live race before deleting an event.", "Delete event",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_activeEvent is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete event '{_activeEvent.Name}' and all of its planned sessions?",
                "Delete event",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var deletedEventId = _activeEvent.Id;
        Events.Remove(_activeEvent);

        foreach (var series in SeriesDefinitions)
            series.EventIds.RemoveAll(id => string.Equals(id, deletedEventId, StringComparison.OrdinalIgnoreCase));

        _activeEvent = Events.FirstOrDefault();

        _suppressUiEvents = true;
        try
        {
            EventSelectCombo.SelectedItem = _activeEvent;
        }
        finally
        {
            _suppressUiEvents = false;
        }

        LoadEventToEditor(_activeEvent);
        UpdateRaceDashboardAvailability();
        RebuildSessionQueueOrder();
        RefreshRaceDayFlow();

        if (_activeEvent is null)
            MainTabs.SelectedItem = EventTab;

        await CaptureAndSaveSettingsAsync();
    }

    private async void BuildQualifiers_Click(object sender, RoutedEventArgs e)
    {
        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            MessageBox.Show(this, "Finish or reset the live race before changing the race-day structure.", "Race in progress",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_activeEvent?.Status == EventStatus.Completed)
        {
            MessageBox.Show(this, "This event is completed. Reopen it before changing qualification rounds.", "Completed event",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_activeEvent is null)
            return;

        SaveEventEditorToModel();

        if (_activeEvent.ClassIds.Count == 0)
        {
            MessageBox.Show(
                this,
                "Add at least one class to this event first.",
                "Create qualifying round",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(QualRoundText.Text.Trim(), out var round) || round is < 1 or > 99)
        {
            MessageBox.Show(this, "Qualification round must be from 1 to 99.", "Create qualifying round",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var order = QualificationOrderExtensions.FromDisplay(SelectedText(QualRoundOrderCombo));

        if (order == QualificationOrder.PreviousRound && round <= 1)
        {
            MessageBox.Show(this, "Previous Round sorting can only be used for Q2 or later.", "Create qualifying round",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (order == QualificationOrder.PreviousRound)
        {
            var priorSessions = _activeEvent.Sessions
                .Where(s => s.SessionType == SessionType.Qualifying &&
                            s.Round == round - 1 &&
                            _activeEvent.ClassIds.Contains(s.ClassId))
                .ToList();

            if (priorSessions.Count == 0 ||
                priorSessions.Any(s => s.ProgressStatus != EventSessionStatus.Completed))
            {
                MessageBox.Show(this,
                    $"Complete every Q{round - 1} heat before building Q{round} with Previous Round sorting. " +
                    "That keeps the new heats based on actual prior-round results rather than the old planned order.",
                    "Previous Round results required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
        }

        var existing = _activeEvent.Sessions
            .Where(s => s.SessionType == SessionType.Qualifying && s.Round == round)
            .ToList();

        if (existing.Any(s => s.ProgressStatus == EventSessionStatus.Completed))
        {
            MessageBox.Show(
                this,
                $"Q{round} contains completed race results and is locked to protect event history. " +
                "Create another qualifying round instead. A dedicated result-correction workflow can be added later.",
                $"Q{round} is locked",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (existing.Count > 0)
        {
            var answer = MessageBox.Show(
                this,
                $"Q{round} already has {existing.Count} heat(s). Rebuilding Q{round} will replace the current heat layout and any manual moves. Continue?",
                $"Rebuild Q{round}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                return;

            foreach (var session in existing)
                _activeEvent.Sessions.Remove(session);
        }

        _activeEvent.QualificationOrder = order;
        var skipped = new List<string>();
        var builtCount = 0;

        foreach (var classId in _activeEvent.ClassIds)
        {
            var cls = Classes.FirstOrDefault(c => c.Id == classId);
            if (cls is null)
                continue;

            var entrants = BuildEntrantsForClass(cls.Id);
            if (entrants.Count == 0)
            {
                skipped.Add(cls.Name);
                continue;
            }

            entrants = ArrangeQualifyingEntrants(cls.Id, entrants, round, order);

            var target = Math.Clamp(cls.MaxRacersPerHeat, 1, 30);
            var heatCount = (int)Math.Ceiling(entrants.Count / (double)target);

            for (var heat = 1; heat <= heatCount; heat++)
            {
                var group = entrants
                    .Skip((heat - 1) * target)
                    .Take(target)
                    .ToList();

                RenumberEntrants(group);

                var session = new EventSessionDefinition
                {
                    ClassId = cls.Id,
                    ClassName = cls.Name,
                    SessionType = SessionType.Qualifying,
                    StartStyle = cls.QualStartStyle,
                    RaceFormat = cls.RaceFormat,
                    TargetLaps = cls.TargetLaps,
                    QualificationOrder = order,
                    Round = round,
                    Heat = heat,
                    ProgressStatus = EventSessionStatus.Planned,
                    Entrants = group
                };
                session.Name = BuildHeatName(session, heatCount);

                _activeEvent.Sessions.Add(session);
                builtCount++;
            }
        }

        RebuildSessionQueueOrder();
        RefreshPlannedSessions();
        RefreshEventRankings();
        EventSelectCombo.Items.Refresh();
        RefreshRaceDayQueue();
        await CaptureAndSaveSettingsAsync();

        FooterStatusText.Text = skipped.Count == 0
            ? $"Q{round} created: {builtCount} heat(s), sorted {order.ToDisplay()}."
            : $"Q{round} created. No drivers were assigned to: {string.Join(", ", skipped)}.";
    }

    private List<SessionEntrant> ArrangeQualifyingEntrants(
        string classId,
        List<SessionEntrant> entrants,
        int round,
        QualificationOrder order)
    {
        return order switch
        {
            QualificationOrder.Random => entrants.OrderBy(_ => Random.Shared.Next()).ToList(),

            QualificationOrder.Ranked => entrants
                .OrderBy(e => e.SeedRank <= 0 ? int.MaxValue : e.SeedRank)
                .ThenBy(e => DisplayName(e), StringComparer.OrdinalIgnoreCase)
                .ToList(),

            QualificationOrder.PreviousRound => ArrangeFromPreviousRound(classId, entrants, round),

            QualificationOrder.Manual => entrants
                .OrderBy(e => DisplayName(e), StringComparer.OrdinalIgnoreCase)
                .ToList(),

            _ => entrants
        };
    }

    private List<SessionEntrant> ArrangeFromPreviousRound(
        string classId,
        List<SessionEntrant> entrants,
        int round)
    {
        if (_activeEvent is null || round <= 1)
            return entrants;

        var resultOrder = GetCompletedRoundDriverOrder(classId, round - 1);
        if (resultOrder.Count > 0)
        {
            var resultIndex = resultOrder
                .Select((driverId, index) => new { driverId, index })
                .ToDictionary(x => x.driverId, x => x.index, StringComparer.OrdinalIgnoreCase);

            return entrants
                .OrderBy(e => resultIndex.TryGetValue(e.DriverId, out var index) ? index : int.MaxValue)
                .ThenBy(e => DisplayName(e), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Fallback when the prior round has not been run yet: retain the planned
        // prior-round order rather than inventing a result.
        var priorOrder = _activeEvent.Sessions
            .Where(s => s.SessionType == SessionType.Qualifying &&
                        s.ClassId == classId &&
                        s.Round == round - 1)
            .OrderBy(s => s.Heat)
            .SelectMany(s => s.Entrants.OrderBy(e => e.CarNumber))
            .Select((entrant, index) => new { entrant.DriverId, index })
            .GroupBy(x => x.DriverId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Min(x => x.index), StringComparer.OrdinalIgnoreCase);

        if (priorOrder.Count == 0)
            return entrants.OrderBy(e => DisplayName(e), StringComparer.OrdinalIgnoreCase).ToList();

        return entrants
            .OrderBy(e => priorOrder.TryGetValue(e.DriverId, out var index) ? index : int.MaxValue)
            .ThenBy(e => DisplayName(e), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async void DeleteQualRound_Click(object sender, RoutedEventArgs e)
    {
        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            MessageBox.Show(this, "Finish or reset the live race before changing the race-day structure.", "Race in progress",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_activeEvent is null)
            return;

        if (!int.TryParse(QualRoundText.Text.Trim(), out var round) || round < 1)
        {
            MessageBox.Show(this, "Enter the qualification round number to delete.", "Delete round",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sessions = _activeEvent.Sessions
            .Where(s => s.SessionType == SessionType.Qualifying && s.Round == round)
            .ToList();

        if (sessions.Count == 0)
        {
            MessageBox.Show(this, $"Q{round} does not exist.", "Delete round",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (sessions.Any(s => s.ProgressStatus == EventSessionStatus.Completed))
        {
            MessageBox.Show(
                this,
                $"Q{round} contains completed results and cannot be deleted from the event history.",
                "Completed round is locked",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(
                this,
                $"Delete Q{round} and all {sessions.Count} of its heat(s)?",
                $"Delete Q{round}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        foreach (var session in sessions)
            _activeEvent.Sessions.Remove(session);

        RebuildSessionQueueOrder();
        RefreshPlannedSessions();
        RefreshEventRankings();
        RefreshRaceDayQueue();
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"Q{round} deleted.";
    }

    private void PlannedSessionGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadSelectedHeatEditor(PlannedSessionGrid.SelectedItem as EventSessionDefinition);
    }


    private void PlannedSessionGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _sessionDragStartPoint = e.GetPosition(null);
        _draggedSession = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as EventSessionDefinition;
        if (_draggedSession?.ProgressStatus is EventSessionStatus.Completed or EventSessionStatus.Skipped)
            _draggedSession = null;
    }

    private void PlannedSessionGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedSession is null || _activeEvent?.Status != EventStatus.Active)
            return;

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _sessionDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _sessionDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var data = new DataObject(HeatSessionDragFormat, _draggedSession);
        DragDrop.DoDragDrop(PlannedSessionGrid, data, DragDropEffects.Move);
        _draggedSession = null;
    }

    private void LoadSelectedHeatEditor(EventSessionDefinition? session)
    {
        SelectedHeatEntrants.Clear();

        if (session is null)
        {
            SelectedHeatTitleText.Text = "HEAT EDITOR";
            SelectedHeatInfoText.Text = "Select a qualification heat.";
            return;
        }

        foreach (var entrant in session.Entrants.OrderBy(e => e.CarNumber))
            SelectedHeatEntrants.Add(entrant);

        var cls = Classes.FirstOrDefault(c => c.Id == session.ClassId);
        var target = cls?.MaxRacersPerHeat ?? 19;

        SelectedHeatTitleText.Text = $"{session.ClassName} • Q{session.Round} • Heat {session.Heat}";
        SelectedHeatInfoText.Text =
            $"{session.StatusDisplay} • {session.Entrants.Count} racer(s) • automatic build target {target}. " +
            "Manual moves may exceed the target.";
    }

    private void HeatEntrantGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeEvent?.Status == EventStatus.Completed ||
            PlannedSessionGrid.SelectedItem is EventSessionDefinition { ProgressStatus: EventSessionStatus.Completed or EventSessionStatus.Skipped })
        {
            _draggedHeatEntrant = null;
            _dragSourceHeat = null;
            return;
        }

        _heatDragStartPoint = e.GetPosition(null);
        _draggedHeatEntrant = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as SessionEntrant;
        _dragSourceHeat = PlannedSessionGrid.SelectedItem as EventSessionDefinition;
    }

    private void HeatEntrantGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            _draggedHeatEntrant is null ||
            _dragSourceHeat is null)
            return;

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _heatDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _heatDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var data = new DataObject(HeatEntrantDragFormat, _draggedHeatEntrant);
        DragDrop.DoDragDrop(HeatEntrantGrid, data, DragDropEffects.Move);

        _draggedHeatEntrant = null;
        _dragSourceHeat = null;
    }

    private void PlannedSessionGrid_DragOver(object sender, DragEventArgs e)
    {
        var target = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as EventSessionDefinition;

        if (e.Data.GetDataPresent(HeatSessionDragFormat))
        {
            e.Effects = _draggedSession is not null && target is not null && target != _draggedSession &&
                        target.ProgressStatus is not EventSessionStatus.Completed and not EventSessionStatus.Skipped
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = IsValidHeatMoveTarget(_dragSourceHeat, target) &&
                    e.Data.GetDataPresent(HeatEntrantDragFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void PlannedSessionGrid_Drop(object sender, DragEventArgs e)
    {
        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            FooterStatusText.Text = "Heat editing is locked while a race is live.";
            return;
        }

        if (_activeEvent?.Status != EventStatus.Active)
        {
            FooterStatusText.Text = "Completed events are read-only.";
            return;
        }

        var target = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as EventSessionDefinition;

        if (e.Data.GetDataPresent(HeatSessionDragFormat) &&
            e.Data.GetData(HeatSessionDragFormat) is EventSessionDefinition moved &&
            target is not null && moved != target &&
            moved.ProgressStatus is not EventSessionStatus.Completed and not EventSessionStatus.Skipped &&
            target.ProgressStatus is not EventSessionStatus.Completed and not EventSessionStatus.Skipped)
        {
            var movable = _activeEvent.Sessions
                .Where(s => s.ProgressStatus is not EventSessionStatus.Completed and not EventSessionStatus.Skipped)
                .OrderBy(s => s.QueueOrder)
                .ToList();
            movable.Remove(moved);
            var targetIndex = movable.IndexOf(target);
            if (targetIndex < 0) targetIndex = movable.Count;
            movable.Insert(targetIndex, moved);
            ApplyRemainingQueueOrder(movable);
            RefreshPlannedSessions(moved.Id);
            PlannedSessionGrid.SelectedItem = moved;
            await CaptureAndSaveSettingsAsync();
            FooterStatusText.Text = $"Moved {moved.Name} in the race-day run order.";
            return;
        }

        if (!IsValidHeatMoveTarget(_dragSourceHeat, target) ||
            e.Data.GetData(HeatEntrantDragFormat) is not SessionEntrant entrant ||
            _dragSourceHeat is null || target is null)
            return;

        var source = _dragSourceHeat;
        if (!source.Entrants.Remove(entrant))
            return;

        target.Entrants.Add(entrant);
        RenumberEntrants(source.Entrants);
        RenumberEntrants(target.Entrants);
        var targetId = target.Id;

        if (source.Entrants.Count == 0)
        {
            var remove = MessageBox.Show(this,
                $"{source.Name} now has no drivers. Remove this empty heat?",
                "Empty heat", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (remove == MessageBoxResult.Yes)
            {
                _activeEvent.Sessions.Remove(source);
                ResequenceHeats(target.ClassId, target.Round);
            }
        }

        RebuildSessionQueueOrder();
        RefreshPlannedSessions(targetId);
        RefreshRaceDayQueue();
        LoadSelectedHeatEditor(target);
        PlannedSessionGrid.SelectedItem = target;
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"{DisplayName(entrant)} moved to {target.ClassName} Q{target.Round} Heat {target.Heat}.";
    }

    private void HeatEntrantGrid_DragOver(object sender, DragEventArgs e)
    {
        var selectedHeat = PlannedSessionGrid.SelectedItem as EventSessionDefinition;
        e.Effects = selectedHeat is not null &&
                    selectedHeat == _dragSourceHeat &&
                    e.Data.GetDataPresent(HeatEntrantDragFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void HeatEntrantGrid_Drop(object sender, DragEventArgs e)
    {
        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            FooterStatusText.Text = "Heat editing is locked while a race is live.";
            return;
        }

        if (_activeEvent?.Status == EventStatus.Completed)
        {
            FooterStatusText.Text = "Completed events are read-only. Reopen the event to edit heat assignments.";
            return;
        }

        var heat = PlannedSessionGrid.SelectedItem as EventSessionDefinition;
        if (heat is null ||
            heat != _dragSourceHeat ||
            e.Data.GetData(HeatEntrantDragFormat) is not SessionEntrant entrant)
            return;

        var targetEntrant = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as SessionEntrant;
        var oldIndex = heat.Entrants.IndexOf(entrant);
        if (oldIndex < 0)
            return;

        heat.Entrants.RemoveAt(oldIndex);

        var newIndex = targetEntrant is null ? heat.Entrants.Count : heat.Entrants.IndexOf(targetEntrant);
        if (newIndex < 0)
            newIndex = heat.Entrants.Count;

        heat.Entrants.Insert(newIndex, entrant);
        RenumberEntrants(heat.Entrants);
        LoadSelectedHeatEditor(heat);
        HeatEntrantGrid.SelectedItem = entrant;

        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text = $"{DisplayName(entrant)} reordered in {heat.Name}.";
    }

    private async void MoveHeatEntrantUp_Click(object sender, RoutedEventArgs e)
    {
        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            FooterStatusText.Text = "Heat editing is locked while a race is live.";
            return;
        }

        if (_activeEvent?.Status == EventStatus.Completed)
        {
            FooterStatusText.Text = "Completed events are read-only. Reopen the event to edit heat assignments.";
            return;
        }

        await MoveSelectedHeatEntrantAsync(-1);
    }

    private async void MoveHeatEntrantDown_Click(object sender, RoutedEventArgs e)
    {
        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            FooterStatusText.Text = "Heat editing is locked while a race is live.";
            return;
        }

        if (_activeEvent?.Status == EventStatus.Completed)
        {
            FooterStatusText.Text = "Completed events are read-only. Reopen the event to edit heat assignments.";
            return;
        }

        await MoveSelectedHeatEntrantAsync(1);
    }

    private async Task MoveSelectedHeatEntrantAsync(int offset)
    {
        var heat = PlannedSessionGrid.SelectedItem as EventSessionDefinition;
        var entrant = HeatEntrantGrid.SelectedItem as SessionEntrant;
        if (heat is null || entrant is null)
            return;

        if (heat.ProgressStatus is EventSessionStatus.Completed or EventSessionStatus.Skipped)
        {
            FooterStatusText.Text = "Completed/skipped heats are locked to preserve event history.";
            return;
        }

        var index = heat.Entrants.IndexOf(entrant);
        var newIndex = index + offset;
        if (index < 0 || newIndex < 0 || newIndex >= heat.Entrants.Count)
            return;

        heat.Entrants.RemoveAt(index);
        heat.Entrants.Insert(newIndex, entrant);
        RenumberEntrants(heat.Entrants);
        LoadSelectedHeatEditor(heat);
        HeatEntrantGrid.SelectedItem = entrant;
        await CaptureAndSaveSettingsAsync();
    }

    private static bool IsValidHeatMoveTarget(
        EventSessionDefinition? source,
        EventSessionDefinition? target)
    {
        return source is not null &&
               target is not null &&
               source != target &&
               source.ProgressStatus is not EventSessionStatus.Completed and not EventSessionStatus.Skipped &&
               target.ProgressStatus is not EventSessionStatus.Completed and not EventSessionStatus.Skipped &&
               source.SessionType == SessionType.Qualifying &&
               target.SessionType == SessionType.Qualifying &&
               source.Round == target.Round &&
               string.Equals(source.ClassId, target.ClassId, StringComparison.OrdinalIgnoreCase);
    }

    private void ResequenceHeats(string classId, int round)
    {
        if (_activeEvent is null)
            return;

        var heats = _activeEvent.Sessions
            .Where(s => s.SessionType == SessionType.Qualifying &&
                        s.ClassId == classId &&
                        s.Round == round)
            .OrderBy(s => s.Heat)
            .ToList();

        for (var i = 0; i < heats.Count; i++)
        {
            heats[i].Heat = i + 1;
            heats[i].Name = BuildHeatName(heats[i], heats.Count);
            RenumberEntrants(heats[i].Entrants);
        }
    }

    private static string BuildHeatName(EventSessionDefinition session, int heatCount)
    {
        return heatCount <= 1
            ? $"{session.ClassName} Q{session.Round}"
            : $"{session.ClassName} Q{session.Round} Heat {session.Heat}";
    }

    private static void RenumberEntrants(IList<SessionEntrant> entrants)
    {
        for (var i = 0; i < entrants.Count; i++)
            entrants[i].CarNumber = i + 1;
    }

    private static string DisplayName(SessionEntrant entrant)
    {
        return string.IsNullOrWhiteSpace(entrant.Nickname)
            ? entrant.DriverName
            : entrant.Nickname;
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
                return match;

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private void LoadSelectedSession_Click(object sender, RoutedEventArgs e)
    {
        if (PlannedSessionGrid.SelectedItem is not EventSessionDefinition session)
        {
            MessageBox.Show(this, "Select a qualification heat first.", "Load heat",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LoadSessionIntoRaceControl(session);
        MainTabs.SelectedItem = RaceTab;
    }

    private void LoadSessionIntoRaceControl(EventSessionDefinition session, bool saveSettings = true)
    {
        _lapSpeech.Stop();
        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            MessageBox.Show(this, "End or reset the current race before loading another heat.", "Race in progress",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_resultAutoSaveInProgress)
        {
            FooterStatusText.Text = "Finishing the previous race result. Please wait a moment before loading another heat.";
            return;
        }

        if (_activeEvent is null)
        {
            MessageBox.Show(this, "Create or select a race-day event first.", "Race Dashboard",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (session.ProgressStatus == EventSessionStatus.Completed && session.Results.Count > 0)
        {
            LoadCompletedSessionForRecall(session);
            ReturnFocusToRaceDashboard();
            return;
        }

        if (_activeEvent.Status == EventStatus.Completed)
        {
            MessageBox.Show(this,
                "This event is completed. You can recall completed heats, or reopen the event before running another session.",
                "Completed event", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var cls = Classes.FirstOrDefault(c => c.Id == session.ClassId);
        if (cls is null)
            return;

        if (_loadedSession is null ||
            !string.Equals(_loadedSession.Id, session.Id, StringComparison.OrdinalIgnoreCase))
        {
            UnknownTransponderHits.Clear();
        }

        PrepareCheckInForSession(session.Id);
        _recallMode = false;
        _lapSpeech.Stop();
        _raceEngine.Reset();
        RecentLaps.Clear();
        Standings.Clear();

        foreach (var other in _activeEvent.Sessions.Where(s =>
                     s.ProgressStatus == EventSessionStatus.Loaded &&
                     !string.Equals(s.Id, session.Id, StringComparison.OrdinalIgnoreCase)))
        {
            other.ProgressStatus = EventSessionStatus.Planned;
        }

        session.ProgressStatus = EventSessionStatus.Loaded;
        _loadedSession = session;
        SessionRacers.Clear();

        foreach (var entrant in session.Entrants.OrderBy(e => e.CarNumber))
        {
            SessionRacers.Add(new RacerEntry
            {
                CarNumber = entrant.CarNumber,
                Enabled = true,
                DriverId = entrant.DriverId,
                DriverName = entrant.DriverName,
                Nickname = entrant.Nickname,
                ClassId = cls.Id,
                ClassName = cls.Name,
                TransponderId = entrant.TransponderId
            });
        }

        _suppressUiEvents = true;
        try
        {
            RaceClassCombo.SelectedItem = cls;
            SelectComboByText(SessionTypeCombo, session.SessionType.ToString());
        }
        finally
        {
            _suppressUiEvents = false;
        }

        RaceEventText.Text = _activeEvent.Name;
        RaceSessionText.Text = session.Name;
        RaceMinutesText.Text = (_loadedSession?.RaceMinutesOverride ?? cls.MinutesFor(_loadedSession?.SessionType ?? GetSelectedSessionType())).ToString("0.###");
        MinimumLapText.Text = cls.MinimumLapSeconds.ToString("0.###");
        StartStyleText.Text = session.StartStyle.ToDisplay();
        UpdateSessionRosterText();
        ShowLoadedRosterStandings(cls);
        ResetDriverLapPanelForSession();

        SetRaceSetupEnabled(true);
        StartRaceButton.IsEnabled = true;
        FalseStartButton.IsEnabled = false;
        StopRaceButton.IsEnabled = false;
        ResetRaceButton.IsEnabled = true;

        HeaderMessageText.Text = $"{_activeEvent.Name} • {session.Name}";
        FooterStatusText.Text = $"Loaded {session.Name} with {SessionRacers.Count} racers.";

        PlannedSessionGrid.Items.Refresh();
        RefreshRaceDayQueue();
        ReturnFocusToRaceDashboard();
        if (saveSettings)
            _ = SaveSettingsSafelyAsync();
    }

    private List<SessionEntrant> BuildEntrantsForClass(string classId)
    {
        if (_activeEvent is null)
            return [];

        var result = new List<SessionEntrant>();
        foreach (var assignment in _activeEvent.DriverAssignments
                     .Where(a => a.ClassId == classId)
                     .GroupBy(a => a.DriverId, StringComparer.OrdinalIgnoreCase)
                     .Select(g => g.First()))
        {
            var driver = Drivers.FirstOrDefault(d => d.Id == assignment.DriverId);
            if (driver is null)
                continue;

            result.Add(new SessionEntrant
            {
                DriverId = driver.Id,
                DriverName = driver.FullName,
                Nickname = driver.Nickname,
                TransponderId = assignment.TransponderId ?? string.Empty,
                SeedRank = assignment.SeedRank
            });
        }

        return result;
    }

    // -----------------------------
    // Race control
    // -----------------------------

    private void RaceClassCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents)
            return;
        _loadedSession = null;
        SessionRacers.Clear();
        UpdateSessionRosterText();
        UpdateRaceDefaultsFromClass();
    }

    private void SessionTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents)
            return;
        _loadedSession = null;
        UpdateRaceDefaultsFromClass(updateTiming: false);
    }

    private void UpdateRaceDefaultsFromClass(bool updateTiming = true)
    {
        if (RaceClassCombo.SelectedItem is not ClassDefinition cls)
            return;
        var type = GetSelectedSessionType();
        if (updateTiming)
        {
            RaceMinutesText.Text = (_loadedSession?.RaceMinutesOverride ?? cls.MinutesFor(_loadedSession?.SessionType ?? GetSelectedSessionType())).ToString("0.###");
            MinimumLapText.Text = cls.MinimumLapSeconds.ToString("0.###");
        }
        StartStyleText.Text = GetConfiguredStartStyle(cls, type).ToDisplay();
        if (string.IsNullOrWhiteSpace(RaceSessionText.Text) || RaceSessionText.Text == "Ad Hoc Race")
            RaceSessionText.Text = $"{cls.Name} {type}";
    }

    private static StartStyle GetConfiguredStartStyle(ClassDefinition cls, SessionType type) => type switch
    {
        SessionType.Qualifying => cls.QualStartStyle,
        SessionType.Race => cls.RaceStartStyle,
        _ => StartStyle.Tone
    };

    private SessionType GetSelectedSessionType()
    {
        return Enum.TryParse<SessionType>(SelectedText(SessionTypeCombo), true, out var type) ? type : SessionType.Race;
    }

    private void LoadClassRacers_Click(object sender, RoutedEventArgs e) => LoadClassRacersInternal(showWarnings: true);

    private void LoadClassRacersInternal(bool showWarnings)
    {
        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            if (showWarnings)
                MessageBox.Show(this, "End or reset the current race before loading another roster.", "Race in progress",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_activeEvent is null || _activeEvent.Status == EventStatus.Completed)
        {
            if (showWarnings)
                MessageBox.Show(this, "Select an active race-day event first.", "Load racers",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _recallMode = false;

        if (RaceClassCombo.SelectedItem is not ClassDefinition cls)
            return;
        var entrants = BuildEntrantsForClass(cls.Id)
            .OrderBy(e => e.SeedRank <= 0 ? int.MaxValue : e.SeedRank)
            .ThenBy(e => string.IsNullOrWhiteSpace(e.Nickname) ? e.DriverName : e.Nickname)
            .ToList();

        if (entrants.Count == 0)
        {
            if (showWarnings)
                MessageBox.Show(this, $"No drivers are assigned to {cls.Name}. Add driver/class assignments first.", "Load racers",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var total = entrants.Count;
        entrants = entrants.Take(30).ToList();
        SessionRacers.Clear();
        for (var i = 0; i < entrants.Count; i++)
        {
            var entrant = entrants[i];
            SessionRacers.Add(new RacerEntry
            {
                CarNumber = i + 1,
                Enabled = true,
                DriverId = entrant.DriverId,
                DriverName = entrant.DriverName,
                Nickname = entrant.Nickname,
                ClassId = cls.Id,
                ClassName = cls.Name,
                TransponderId = entrant.TransponderId
            });
        }

        _loadedSession = null;
        RaceEventText.Text = _activeEvent?.Name ?? "Ad Hoc";
        RaceSessionText.Text = $"{cls.Name} {GetSelectedSessionType()}";
        UpdateRaceDefaultsFromClass();
        UpdateSessionRosterText();
        if (total > SessionRacers.Count)
            FooterStatusText.Text = $"Loaded the race roster for {cls.Name}. Additional class entries remain available when generating heats/mains.";
        else
            FooterStatusText.Text = $"Loaded {total} driver(s) for {cls.Name}.";
    }

    private void UpdateSessionRosterText()
    {
        SessionRosterText.Text = SessionRacers.Count == 0
            ? "No session roster loaded."
            : $"{SessionRacers.Count} racers • " + string.Join(" • ", SessionRacers.Take(5).Select(r => $"{r.CarNumber}:{r.DisplayName}")) +
              (SessionRacers.Count > 5 ? " …" : string.Empty);
    }

    private async void StartRaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_practiceActive)
        {
            MessageBox.Show(this,
                "Practice is active. Stop Practice before starting a race.",
                "Race locked by Practice",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_resultAutoSaveInProgress)
        {
            FooterStatusText.Text = "The completed result is still being saved. Please wait a moment.";
            return;
        }

        if (_loadedSession?.ProgressStatus == EventSessionStatus.Completed)
        {
            MessageBox.Show(this, "That heat is already complete. Load the Current Race from the event queue.", "Race Dashboard",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_recallMode)
        {
            MessageBox.Show(this, "This dashboard is showing a recalled result. Load a planned heat before starting.", "Race Dashboard",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_activeEvent is null || _activeEvent.Status == EventStatus.Completed)
        {
            MessageBox.Show(this, "Create/select an active race-day event before starting a race.", "Race Dashboard",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (SessionRacers.Count == 0)
                throw new InvalidOperationException("Load racers into the session before starting.");

            if (!double.TryParse(MinimumLapText.Text, out var minLapSeconds) || minLapSeconds < 0)
                throw new InvalidOperationException("Minimum lap time must be zero or greater.");

            var cls = RaceClassCombo.SelectedItem as ClassDefinition
                      ?? Classes.FirstOrDefault(c => c.Id == _loadedSession?.ClassId)
                      ?? throw new InvalidOperationException("Select a valid class before starting.");

            var raceFormat = _loadedSession?.RaceFormat ?? cls.FormatFor(GetSelectedSessionType());
            var targetLaps = _loadedSession?.TargetLaps > 0
                ? _loadedSession.TargetLaps
                : cls.TargetLaps;

            var minutes = cls.MinutesFor(GetSelectedSessionType());
            if (raceFormat == RaceFormat.Timed &&
                (!double.TryParse(RaceMinutesText.Text, out minutes) || minutes <= 0))
            {
                throw new InvalidOperationException("Enter a race duration greater than zero minutes.");
            }

            if (raceFormat == RaceFormat.Laps && targetLaps <= 0)
                throw new InvalidOperationException("The class must have a target lap count greater than zero.");

            var transponderOwners = SessionRacers
                .SelectMany(r => SplitTransponders(r.TransponderId).Select(t => new { Transponder = t, Racer = r.DisplayName }))
                .GroupBy(x => x.Transponder, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Select(x => x.Racer).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                .Select(g => g.Key)
                .ToArray();
            if (transponderOwners.Length > 0)
                throw new InvalidOperationException($"Duplicate transponder ID assigned to multiple racers: {string.Join(", ", transponderOwners)}");

            var sessionType = GetSelectedSessionType();
            var startStyle = _loadedSession?.StartStyle ?? GetConfiguredStartStyle(cls, sessionType);

            var missingCheckIns = _raceCheckIn.MissingNames(SessionRacers);
            if (missingCheckIns.Count > 0)
            {
                var message = RaceCheckInState.WarningMessage(missingCheckIns) +
                    "\n\nStart anyway? Choose No to wait for check-in.";
                var answer = MessageBox.Show(this, message, "Racers not checked in",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                ReturnFocusToRaceDashboard();
                if (answer != MessageBoxResult.Yes) return;
            }

            _raceAudio.Reset();
            var toneTime = DateTimeOffset.Now;
            _toneService.Play(_settings.StartTone, _settings.StartToneLength, _settings.StartToneFile);
            _endTonePlayed = false;
            _lapSpeech.Stop();
            _raceEngine.Start(
                SessionRacers,
                raceFormat == RaceFormat.Timed ? TimeSpan.FromMinutes(minutes) : TimeSpan.Zero,
                targetLaps,
                raceFormat,
                TimeSpan.FromSeconds(minLapSeconds),
                sessionType,
                startStyle,
                toneTime);

            if (_loadedSession is not null)
            {
                _loadedSession.ProgressStatus = EventSessionStatus.Running;
                _loadedSession.StartedAt = toneTime;
                _loadedSession.CompletedAt = null;
                _loadedSession.Results.Clear();
                _loadedSession.Laps.Clear();
                PlannedSessionGrid.Items.Refresh();
                RefreshRaceDayQueue();
            }

            RecentLaps.Clear();
            ClearDriverPassingHighlight();
            _resultAutoSaved = false;
            _resultAutoSaveInProgress = false;
            _lastRenderedRaceState = RaceState.Ready;
            _raceTimer.Start();
            SetRaceSetupEnabled(false);
            StartRaceButton.IsEnabled = false;
            FalseStartButton.IsEnabled = true;
            StopRaceButton.IsEnabled = true;

            await CaptureAndSaveSettingsAsync();
            RefreshRaceUi(DateTimeOffset.Now);
            LastPassingText.Text = startStyle switch
            {
                StartStyle.Tone => "Session started at the tone. Waiting for lap passings…",
                StartStyle.LeaderCrossing => $"Session armed. Car {_raceEngine.LeaderCarNumber} is the leader; all clocks start when that car crosses.",
                StartStyle.IndividualCrossing => "Session armed. Each racer's first crossing starts only that racer's clock.",
                _ => "Session started."
            };
            FooterStatusText.Text = LastPassingText.Text;
            ReturnFocusToRaceDashboard();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot start session", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void FalseStartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_raceEngine.State is RaceState.Ready or RaceState.Complete)
            return;
        _lapSpeech.Stop();
        _raceEngine.FalseStart();
        if (_loadedSession is not null && _loadedSession.ProgressStatus == EventSessionStatus.Running)
            _loadedSession.ProgressStatus = EventSessionStatus.Loaded;
        PlannedSessionGrid.Items.Refresh();
        RefreshRaceDayQueue();
        _raceTimer.Stop();
        RecentLaps.Clear();
        Standings.Clear();
        ClearDriverPassingHighlight();
        _resultAutoSaved = false;
        _endTonePlayed = false;
        SetRaceSetupEnabled(true);
        StartRaceButton.IsEnabled = true;
        FalseStartButton.IsEnabled = false;
        StopRaceButton.IsEnabled = false;
        RefreshRaceUi(DateTimeOffset.Now);
        LastPassingText.Text = "FALSE START — all session timing and laps were cleared. Press START when ready to retry.";
        FooterStatusText.Text = LastPassingText.Text;
        ReturnFocusToRaceDashboard();
    }

    private async void StopRaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_raceEngine.State is RaceState.Ready or RaceState.Complete)
            return;
        if (MessageBox.Show(this, "End the current race now? No more passings will be counted.", "End race",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _lapSpeech.Stop();
        _raceEngine.Stop(DateTimeOffset.Now);
        _raceTimer.Stop();
        RefreshRaceUi(DateTimeOffset.Now);
        SetRaceSetupEnabled(true);
        StartRaceButton.IsEnabled = _loadedSession is null && _activeEvent?.Status == EventStatus.Active;
        FalseStartButton.IsEnabled = false;
        StopRaceButton.IsEnabled = false;
        await AutoSaveCompletedRaceAsync();
    }

    private void ResetRaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_resultAutoSaveInProgress)
        {
            FooterStatusText.Text = "The completed result is still being saved. Please wait a moment.";
            return;
        }

        if (_recallMode)
        {
            _recallMode = false;
            _lapSpeech.Stop();
            _raceEngine.Reset();
            Standings.Clear();
            RecentLaps.Clear();
            RaceStateText.Text = "READY";
            RaceClockText.Text = _loadedSession?.RaceFormat == RaceFormat.Laps
                ? "0:00.0"
                : RaceEngine.FormatClock(TimeSpan.FromMinutes(ParseRaceMinutesOrDefault()));
            HeaderRaceStateText.Text = "READY";
            LastPassingText.Text = "Waiting for a passing…";
            StartRaceButton.IsEnabled = _activeEvent?.Status == EventStatus.Active &&
                                        _loadedSession is not null &&
                                        _loadedSession.ProgressStatus != EventSessionStatus.Completed;
            ResetRaceButton.IsEnabled = true;
            FooterStatusText.Text = "Recalled result cleared.";
            return;
        }

        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
        {
            if (MessageBox.Show(this, "Reset the current session and clear all timing?", "Reset",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        _lapSpeech.Stop();
        _raceEngine.Reset();
        if (_loadedSession is not null &&
            _loadedSession.ProgressStatus is EventSessionStatus.Running or EventSessionStatus.Loaded)
        {
            _loadedSession.ProgressStatus = EventSessionStatus.Loaded;
            _loadedSession.StartedAt = null;
            _loadedSession.CompletedAt = null;
            _loadedSession.Results.Clear();
            _loadedSession.Laps.Clear();
        }

        _raceTimer.Stop();
        RecentLaps.Clear();
        Standings.Clear();
        ClearDriverPassingHighlight();
        _resultAutoSaved = false;
        _endTonePlayed = false;
        SetRaceSetupEnabled(true);
        StartRaceButton.IsEnabled = _activeEvent?.Status == EventStatus.Active;
        FalseStartButton.IsEnabled = false;
        StopRaceButton.IsEnabled = false;
        _lastRenderedRaceState = RaceState.Ready;
        RefreshRaceUi(DateTimeOffset.Now);
        LastPassingText.Text = "Waiting for a passing…";
        PlannedSessionGrid.Items.Refresh();
        RefreshRaceDayQueue();
        FooterStatusText.Text = "Session reset.";
        _ = SaveSettingsSafelyAsync();
    }

    private void SetRaceSetupEnabled(bool enabled)
    {
        RaceSessionText.IsEnabled = enabled;
        RaceClassCombo.IsEnabled = enabled;
        SessionTypeCombo.IsEnabled = enabled;
        RaceMinutesText.IsEnabled = enabled;
        MinimumLapText.IsEnabled = enabled;
    }

    private void RaceTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.Now;
        _raceEngine.Tick(now);
        UpdateMasterEndTone(now);
        UpdateRaceAudio(now);
        RefreshRaceUi(now);
        if (_raceEngine.State == RaceState.Complete)
        {
            _raceTimer.Stop();
            _ = AutoSaveCompletedRaceAsync();
        }
    }

    private void RefreshRaceUi(DateTimeOffset now)
    {
        if (_recallMode)
        {
            RenderRecalledRace();
            return;
        }

        var state = _raceEngine.State;

        if (state == RaceState.Complete && _lastRenderedRaceState != RaceState.Complete && _raceEngine.AllDriversFinished)
        {
            _lapSpeech.Stop();
            _lapSpeech.Enqueue("All drivers are done.", _settings.RaceAnnouncementRate,
                _settings.LapSpeechVolume, _settings.LapSpeechVoice);
        }

        RaceStateText.Text = state switch
        {
            RaceState.Ready => "READY",
            RaceState.Armed => _raceEngine.StartStyle == StartStyle.LeaderCrossing ? "ARMED — WAITING LEADER" : "ARMED — INDIVIDUAL STARTS",
            RaceState.Running when _raceEngine.LapTargetReached => "FINISH — FINAL LAPS",
            RaceState.Running => _raceEngine.PracticeMode ? "PRACTICE" : "RACING",
            RaceState.TimeExpired => _raceEngine.StartStyle == StartStyle.IndividualCrossing ? "INDIVIDUAL CLOCKS — FINAL LAPS" : "TIME EXPIRED — FINAL LAP",
            RaceState.FalseStart => "FALSE START",
            RaceState.Complete => "COMPLETE",
            _ => state.ToString().ToUpperInvariant()
        };
        HeaderRaceStateText.Text = RaceStateText.Text;

        RaceClockText.Text = state switch
        {
            RaceState.Ready or RaceState.FalseStart when _loadedSession?.RaceFormat == RaceFormat.Laps => "0:00.0",
            RaceState.Ready or RaceState.FalseStart => RaceEngine.FormatClock(TimeSpan.FromMinutes(ParseRaceMinutesOrDefault())),
            _ when _raceEngine.PracticeMode => "+" + RaceEngine.FormatClock(_raceEngine.GetMasterElapsed(now)),
            _ when _raceEngine.IsLappedRace => RaceEngine.FormatClock(_raceEngine.GetMasterElapsed(now)),
            _ => RaceEngine.FormatMasterClock(_raceEngine.GetRemaining(now))
        };

        ReplaceStandings(_raceEngine.GetStandings(now));
        RefreshRoundProjection();

        var fastest = Standings.Where(s => s.IsFastestLap && s.BestLap != "—").ToList();
        FastestSessionText.Text = fastest.Count == 0
            ? "Fastest: —"
            : "FASTEST: " + string.Join(" / ", fastest.Select(f => $"{f.DriverName} {f.BestLap} (L{f.BestLapNumber})"));

        if (state == RaceState.Complete)
        {
            SetRaceSetupEnabled(true);
            StartRaceButton.IsEnabled = _activeEvent?.Status == EventStatus.Active && _loadedSession is null;
            FalseStartButton.IsEnabled = false;
            StopRaceButton.IsEnabled = false;
        }

        if (state != _lastRenderedRaceState)
        {
            HeaderMessageText.Text = state switch
            {
                RaceState.Ready => "Configure the session and press START.",
                RaceState.Armed when _raceEngine.StartStyle == StartStyle.LeaderCrossing => $"Waiting for leader Car {_raceEngine.LeaderCarNumber} to cross the line.",
                RaceState.Armed => "Each racer's first crossing starts that racer's own clock.",
                RaceState.Running when _raceEngine.StartStyle == StartStyle.IndividualCrossing => "Individual clocks are active; standings show each racer's own remaining time.",
                RaceState.Running => "Session is live.",
                RaceState.TimeExpired when _raceEngine.StartStyle == StartStyle.IndividualCrossing => "Racers finish only after their own clock expires and they cross again.",
                RaceState.TimeExpired => "Master clock expired. Each racer finishes on the next accepted pass.",
                RaceState.FalseStart => "False start — timing cleared.",
                RaceState.Complete => "Session complete. Additional passings are ignored.",
                _ => string.Empty
            };
            _lastRenderedRaceState = state;
        }
        HoldFinishedSpectatorFrame();
    }

    private void ReplaceStandings(IReadOnlyList<StandingRow> rows)
    {
        var now = DateTimeOffset.Now;

        var expired = _passingHighlightUntilByCar
            .Where(pair => now >= pair.Value)
            .Select(pair => pair.Key)
            .ToList();
        foreach (var carNumber in expired)
            _passingHighlightUntilByCar.Remove(carNumber);

        var incomingCars = rows.Select(r => r.CarNumber).ToHashSet();

        // Remove racers that are no longer in this heat.
        for (var i = Standings.Count - 1; i >= 0; i--)
        {
            if (!incomingCars.Contains(Standings[i].CarNumber))
                Standings.RemoveAt(i);
        }

        // Reuse the existing row object whenever possible. This is much faster
        // than clearing/recreating the DataGrid every 100 ms and allows the
        // crossing/new-best triggers to react immediately via INotifyPropertyChanged.
        for (var targetIndex = 0; targetIndex < rows.Count; targetIndex++)
        {
            var snapshot = rows[targetIndex];
            var existing = Standings.FirstOrDefault(r => r.CarNumber == snapshot.CarNumber);

            if (existing is null)
            {
                snapshot.IsLastPassing = IsPassingHighlightActive(snapshot.CarNumber, now);
                snapshot.IsMinimumLapViolationGlow = IsMinimumLapGlowActive(snapshot.CarNumber, now);
                snapshot.IsCheckedIn = _checkedInCars.Contains(snapshot.CarNumber);
                snapshot.ManualActionText =
                    _raceEngine.State is RaceState.Ready or RaceState.FalseStart
                        ? "CHECK IN"
                        : "PASS";
                Standings.Insert(Math.Min(targetIndex, Standings.Count), snapshot);
                existing = snapshot;
            }
            else
            {
                existing.CopyTimingFrom(snapshot);
                existing.IsLastPassing = IsPassingHighlightActive(existing.CarNumber, now);
                existing.IsMinimumLapViolationGlow = IsMinimumLapGlowActive(existing.CarNumber, now);
                existing.IsCheckedIn = _checkedInCars.Contains(existing.CarNumber);
                existing.ManualActionText =
                    _raceEngine.State is RaceState.Ready or RaceState.FalseStart
                        ? "CHECK IN"
                        : "PASS";

                var currentIndex = Standings.IndexOf(existing);
                if (currentIndex != targetIndex && currentIndex >= 0 && targetIndex < Standings.Count)
                    Standings.Move(currentIndex, targetIndex);
            }
        }

        if (_passingHighlightUntilByCar.Count == 0)
            _passingHighlightTimer?.Stop();
        if (_minimumLapGlowUntilByCar.Count == 0)
            _minimumLapGlowTimer?.Stop();
    }

    private void ManualCarButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: not null } button && int.TryParse(button.Tag.ToString(), out var carNumber))
            RegisterManualPassing(carNumber, "Manual Button");
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.IsRepeat || MainTabs.SelectedItem != RaceTab || IsEditingText())
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        int? carNumber = null;

        if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt &&
            (modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) == ModifierKeys.None)
        {
            var digit = KeyToDigit(key);
            if (digit.HasValue && digit.Value is >= 0 and <= 9)
                carNumber = 10 + digit.Value;
        }
        else if (modifiers == ModifierKeys.None)
        {
            var digit = KeyToDigit(key);
            if (digit.HasValue && digit.Value is >= 1 and <= 9)
                carNumber = digit.Value;
        }

        if (carNumber is null)
            return;
        e.Handled = true;
        RegisterManualPassing(carNumber.Value, "Keyboard");
    }

    private void RegisterManualPassing(int carNumber, string source) =>
        RegisterPassingForCar(carNumber, source, DateTimeOffset.Now);

    private void RegisterPassingForCar(
        int carNumber,
        string source,
        DateTimeOffset timestamp)
    {
        if (_recallMode)
        {
            FooterStatusText.Text = "Recalled results are read-only. Load a planned heat to score a race.";
            return;
        }

        // Before START, the same manual PASS button / keyboard car number acts
        // as driver check-in. Decoder protocol parsers use this same car-detection
        // path once they resolve a transponder to a car number.
        if (_raceEngine.State is RaceState.Ready or RaceState.FalseStart)
        {
            if (MarkDriverCheckedIn(carNumber, source))
            {
                _toneService.Play(
                    _settings.PassingTone,
                    _settings.PassingToneLength,
                    _settings.PassingToneFile);

                Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
            }
            return;
        }

        UpdateMasterEndTone(timestamp);
        var lapTargetWasReached = _raceEngine.LapTargetReached;
        var result = _raceEngine.RegisterPassing(carNumber, source, timestamp);
        if (!lapTargetWasReached && _raceEngine.LapTargetReached && !_endTonePlayed)
        {
            _endTonePlayed = true;
            _lapSpeech.Stop();
            _toneService.Play(_settings.EndTone, _settings.EndToneLength, _settings.EndToneFile);
        }

        var shouldHighlightPassing = result.Code is PassingResultCode.StartCrossing or
            PassingResultCode.LapAccepted or
            PassingResultCode.MinimumLapRejected or
            PassingResultCode.PreStartRejected;

        // Check-in and visual passing feedback happen before the heavier standings
        // recalculation. This gives the operator an immediate response on the same
        // UI cycle as the crossing.
        if (shouldHighlightPassing)
        {
            MarkDriverCheckedIn(carNumber, source, announce: false);
            MarkDriverPassing(carNumber);
            ApplyImmediatePassingVisual(carNumber, result);
            Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
        }

        if (result.Code == PassingResultCode.MinimumLapRejected)
        {
            MarkMinimumLapViolation(carNumber);
            RefreshDriverLapPanel();
            _toneService.Play(_settings.MinimumLapTone, _settings.MinimumLapToneLength, _settings.MinimumLapToneFile);
            Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
        }
        else if (result.Accepted || result.Code == PassingResultCode.PreStartRejected)
        {
            var firstCrossing = !_raceEngine.PracticeMode && result.Accepted && _raceAudio.FirstCrossing(carNumber);
            if (result.Accepted && _raceEngine.GetStandings(timestamp).Any(r => r.CarNumber == carNumber && r.IsFinished) && _settings.RaceDriverFinishToneEnabled)
            {
                if (lapTargetWasReached || !_raceEngine.IsLappedRace)
                    _toneService.Play(_settings.DriverFinishTone, _settings.DriverFinishToneLength, _settings.DriverFinishToneFile);
                // The timed-race end cue belongs to master zero, not the final driver.
                if (_raceEngine.State == RaceState.Complete)
                {
                    if (_raceEngine.IsLappedRace) _endTonePlayed = true;
                    _lapSpeech.Stop();
                }
            }
            else if (firstCrossing && _settings.RaceFirstCrossingToneEnabled)
                _toneService.Play(_settings.FirstCrossingTone, _settings.FirstCrossingToneLength, _settings.FirstCrossingToneFile);
            else
            {
                var spokeLap = result.Code == PassingResultCode.LapAccepted && result.Lap is not null
                    && AnnounceRaceLap(result.Lap);
                if (!spokeLap)
                    _toneService.Play(_settings.PassingTone, _settings.PassingToneLength, _settings.PassingToneFile);
            }
        }

        if (!result.Accepted)
        {
            LastPassingText.Text = result.Message;
            FooterStatusText.Text = result.Message;
            RefreshRaceUi(DateTimeOffset.Now);
            return;
        }

        if (result.Lap is not null)
        {
            RecentLaps.Insert(0, result.Lap);
            while (RecentLaps.Count > 250)
                RecentLaps.RemoveAt(RecentLaps.Count - 1);

            LastPassingText.Text = $"CAR {carNumber} • {result.Lap.DriverName} • LAP {result.Lap.LapNumber} • {RaceEngine.FormatLap(result.Lap.LapTime)}" +
                                   (result.Lap.Finished ? " • FINISHED" : string.Empty);
        }
        else
        {
            LastPassingText.Text = result.Message;
        }

        FooterStatusText.Text = result.Message;
        RefreshDriverLapPanel();
        RefreshRaceUi(DateTimeOffset.Now);

        // Force the final timing snapshot (position/gaps/averages) to paint now,
        // not on the next 100 ms dashboard timer tick.
        Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));

        if (_raceEngine.State == RaceState.Complete)
        {
            _raceTimer.Stop();
            _ = AutoSaveCompletedRaceAsync();
        }
    }

    private void UndoLastPassing_Click(object sender, RoutedEventArgs e)
    {
        if (_recallMode || _loadedSession?.ProgressStatus == EventSessionStatus.Completed)
        {
            FooterStatusText.Text = "Completed/recalled results are locked. Reopen and rerun the heat if a correction is required.";
            return;
        }

        _lapSpeech.Stop();
        var result = _raceEngine.UndoLastPassing(DateTimeOffset.Now);
        FooterStatusText.Text = result.Message;
        LastPassingText.Text = result.Message;
        if (!result.Accepted)
            return;

        RebuildRecentLaps();
        _resultAutoSaved = false;
        if (_raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired)
            _raceTimer.Start();
        RefreshRaceUi(DateTimeOffset.Now);
    }

    private void RebuildRecentLaps()
    {
        RecentLaps.Clear();
        foreach (var lap in _raceEngine.Laps.OrderByDescending(l => l.Sequence).Take(250))
            RecentLaps.Add(lap);
    }

    private async void SaveResults_Click(object sender, RoutedEventArgs e)
    {
        if (_resultAutoSaveInProgress)
        {
            FooterStatusText.Text = "The completed result is already being saved.";
            return;
        }

        if (_raceEngine.State == RaceState.Complete &&
            _loadedSession is not null &&
            _loadedSession.ProgressStatus != EventSessionStatus.Completed)
        {
            await PersistCompletedSessionAsync(_loadedSession);
        }

        var paths = await SaveResultsInternalAsync();
        if (paths is null)
            return;

        FooterStatusText.Text = $"Results saved: {Path.GetFileName(paths.Value.CsvPath)}";
        if (_raceEngine.State == RaceState.Complete)
            _resultAutoSaved = true;
    }

    private void OpenResults_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_resultService.ResultsDirectory);
        OpenFolder(_resultService.ResultsDirectory);
    }

    private async Task AutoSaveCompletedRaceAsync()
    {
        if (_resultAutoSaved || _resultAutoSaveInProgress || _raceEngine.State != RaceState.Complete)
            return;

        _resultAutoSaveInProgress = true;
        var completedSession = _loadedSession;

        try
        {
            // The event record is the primary historical record. Capture it first so a
            // slow CSV/JSON export cannot let the race director accidentally load the
            // next heat and redirect the finished result.
            if (completedSession is not null)
                await PersistCompletedSessionAsync(completedSession);

            var result = await SaveResultsInternalAsync();
            _resultAutoSaved = result is not null;
        }
        finally
        {
            _resultAutoSaveInProgress = false;
            RefreshRaceDayQueue();

            // Keep the completed heat on-screen. The operator moves to another
            // heat explicitly with Previous Race / Next Race or the dropdown.
            RefreshDriverLapPanel();
        }
    }

    private async Task<(string JsonPath, string CsvPath)?> SaveResultsInternalAsync()
    {
        if (_recallMode && _loadedSession is not null && _activeEvent is not null)
        {
            try
            {
                var cls = Classes.FirstOrDefault(c => c.Id == _loadedSession.ClassId);
                var duration = (_loadedSession.RaceFormat == RaceFormat.Laps)
                    ? TimeSpan.Zero
                    : TimeSpan.FromMinutes(_loadedSession.RaceMinutesOverride ?? cls?.MinutesFor(_loadedSession.SessionType) ?? ParseRaceMinutesOrDefault());
                return await _resultService.SaveAsync(
                    _activeEvent.Name,
                    _loadedSession.ClassName,
                    _loadedSession.Name,
                    _loadedSession.SessionType,
                    _loadedSession.StartStyle,
                    _loadedSession.StartedAt,
                    duration,
                    _loadedSession.Results,
                    _loadedSession.Laps,
                    _loadedSession.Breakouts);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Save recalled results", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }

        try
        {
            var cls = RaceClassCombo.SelectedItem as ClassDefinition;
            var result = await _resultService.SaveAsync(
                RaceEventText.Text.Trim(),
                cls?.Name ?? "Class",
                RaceSessionText.Text.Trim(),
                _raceEngine.SessionType,
                _raceEngine.StartStyle,
                _raceEngine.StartedAt,
                _raceEngine.Duration,
                _raceEngine.GetStandings(DateTimeOffset.Now),
                _raceEngine.Laps,
                _raceEngine.Breakouts);
            return result;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save results", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private static IEnumerable<string> SplitTransponders(string? value) =>
        (value ?? string.Empty).Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // -----------------------------
    // Decoder / connection console
    // -----------------------------

    private void ConnectionTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        if (SelectedText(ConnectionTypeCombo).StartsWith("I-Lap", StringComparison.Ordinal) || SelectedText(ConnectionTypeCombo).StartsWith("Trackmate", StringComparison.Ordinal))
        {
            BaudCombo.Text = "9600";
            SelectComboByText(DataBitsCombo, "8");
            SelectComboByText(ParityCombo, "None");
            SelectComboByText(StopBitsCombo, "1");
            SelectComboByText(HandshakeCombo, "None");
        }
        UpdateConnectionPanelVisibility();
    }

    private void UpdateConnectionPanelVisibility()
    {
        if (SerialGroup is null || LanGroup is null)
            return;
        var mode = SelectedText(ConnectionTypeCombo);
        SerialGroup.Visibility = (mode.StartsWith("USB", StringComparison.OrdinalIgnoreCase) || mode.StartsWith("I-Lap", StringComparison.Ordinal) || mode.StartsWith("Trackmate", StringComparison.Ordinal)) ? Visibility.Visible : Visibility.Collapsed;
        OpenStintGroup.Visibility = mode.StartsWith("OpenStint", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        LanGroup.Visibility = mode.StartsWith("LAN", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshPorts_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private void RefreshPorts()
    {
        if (PortCombo is null)
            return;
        var wanted = PortCombo.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(wanted))
            wanted = _settings.LastComPort;

        PortCombo.Items.Clear();
        foreach (var port in PortDiscoveryService.GetPorts())
            PortCombo.Items.Add(port);

        if (!string.IsNullOrWhiteSpace(wanted) && PortCombo.Items.Contains(wanted))
            PortCombo.SelectedItem = wanted;
        else if (PortCombo.Items.Count > 0)
            PortCombo.SelectedIndex = 0;
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ConnectButton.IsEnabled = false;
            var factory = BuildTransportFactory();
            await _connection.StartAsync(factory, autoReconnect: true);
            DisconnectButton.IsEnabled = true;
            await CaptureAndSaveSettingsAsync();
        }
        catch (Exception ex)
        {
            DecoderDiagnostics.Write("CONNECT ERROR", "Connection request failed", ex);
            MessageBox.Show(this, ex.Message + "\n\nUse Decoder → OPEN LOGS for diagnostic details.", "Connection failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            ConnectButton.IsEnabled = true;
            DisconnectButton.IsEnabled = false;
        }
    }

    private Func<IByteTransport> BuildTransportFactory()
    {
        var mode = SelectedText(ConnectionTypeCombo);
        if (mode.StartsWith("USB", StringComparison.OrdinalIgnoreCase) || mode.StartsWith("I-Lap", StringComparison.Ordinal) || mode.StartsWith("Trackmate", StringComparison.Ordinal))
        {
            var portName = PortCombo.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(portName))
                throw new InvalidOperationException("No COM port is selected. Connect the USB/serial device and press Refresh.");
            if (!int.TryParse(EditableComboText(BaudCombo), out var baud) || baud <= 0)
                throw new InvalidOperationException("Enter a valid baud rate.");
            if (!int.TryParse(SelectedText(DataBitsCombo), out var dataBits))
                throw new InvalidOperationException("Select valid data bits.");
            var parity = Enum.Parse<Parity>(SelectedText(ParityCombo), true);
            var stopBits = ParseStopBits(SelectedText(StopBitsCombo));
            var handshake = Enum.Parse<Handshake>(SelectedText(HandshakeCombo), true);
            return () => new SerialUsbTransport(portName, baud, dataBits, parity, stopBits, handshake, mode.StartsWith("I-Lap", StringComparison.Ordinal), mode.StartsWith("Trackmate", StringComparison.Ordinal));
        }

        if (mode.StartsWith("LAN", StringComparison.OrdinalIgnoreCase))
        {
            var host = HostText.Text.Trim();
            if (string.IsNullOrWhiteSpace(host))
                throw new InvalidOperationException("Enter the decoder IP address or host name.");
            if (!int.TryParse(TcpPortText.Text, out var port) || port is < 1 or > 65535)
                throw new InvalidOperationException("Enter a TCP port from 1 to 65535.");
            return () => new TcpTransport(host, port);
        }

        if (mode.StartsWith("OpenStint", StringComparison.Ordinal))
        {
            var host = OpenStintHostText.Text.Trim();
            if (string.IsNullOrWhiteSpace(host) || host.Any(char.IsWhiteSpace) || host.Contains('/') || host.Contains('*'))
                throw new InvalidOperationException("Enter the OpenStint decoder host name or IP address.");
            if (!int.TryParse(OpenStintPortText.Text, out var port) || port is < 1 or > 65535)
                throw new InvalidOperationException("Enter an OpenStint port from 1 to 65535 (default 5556).");
            return () => new OpenStintTransport(host, port);
        }
        return () => new SimulatorTransport();
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        DisconnectButton.IsEnabled = false;
        await _connection.StopAsync();
        ConnectButton.IsEnabled = true;
    }

    private void Connection_StatusChanged(object? sender, TransportStatusEventArgs e)
    {
        if (e.State is TransportState.Connecting or TransportState.Disconnected or TransportState.Error)
            _decoderIngress.ResetConnection();
        Dispatcher.Invoke(() =>
        {
            DecoderStatusText.Text = e.Message + (e.State == TransportState.Error ? " — see OPEN LOGS" : "");
            if (DecoderDiagnostics.LastWriteError is { } logError)
                FooterStatusText.Text = $"Unable to save decoder diagnostics: {logError}";
            if (e.State is TransportState.Connecting or TransportState.Disconnected)
                DecoderProtocolText.Text = "Protocol: Auto — waiting for data";
            DecoderStatusDot.Fill = e.State switch
            {
                TransportState.Connected => new SolidColorBrush(Color.FromRgb(53, 199, 111)),
                TransportState.Connecting => new SolidColorBrush(Color.FromRgb(234, 185, 72)),
                TransportState.Error => new SolidColorBrush(Color.FromRgb(229, 59, 59)),
                _ => new SolidColorBrush(Color.FromRgb(107, 114, 128))
            };

            if (e.State == TransportState.Connected)
            {
                ConnectButton.IsEnabled = false;
                DisconnectButton.IsEnabled = true;
            }
            else if (e.State == TransportState.Disconnected && !_closing)
            {
                ConnectButton.IsEnabled = true;
                DisconnectButton.IsEnabled = false;
            }
        });
    }

    private void Connection_BytesReceived(object? sender, BytesReceivedEventArgs e)
    {
        // Timestamp immediately at ingress. Do not perform formatting, parsing,
        // file I/O, or UI work on the transport callback thread.
        var now = DateTimeOffset.Now;
        var transportName = _connection.CurrentTransport?.DisplayName ?? "Unknown";

        _trafficLogger.AppendReceived(transportName, e.Data, now);
        _decoderIngress.Enqueue(transportName, e.Data, now);
    }

    private void DecoderIngress_PacketFormatted(DecoderIngressService.FormattedPacket packet)
    {
        // Scoring has normal dispatcher priority; traffic rendering stays lower.
        if (packet.Passings.Count > 0)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_closing) return;
                foreach (var passing in packet.Passings)
                    ObserveDecoderPassing(passing, $"{packet.Transport} • {passing.Protocol}");
            }));
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            DecoderProtocolText.Text = $"Protocol: {packet.Protocol}";
            _chunkCount++;
            _byteCount += packet.Bytes;
            Traffic.Add(new TrafficRow
            {
                Time = packet.Timestamp.ToString("HH:mm:ss.fff"),
                Transport = packet.Transport,
                Bytes = packet.Bytes,
                Hex = packet.Hex,
                Ascii = packet.Ascii,
                DecodeSummary = packet.DecodeSummary
            });

            while (Traffic.Count > 1000)
                Traffic.RemoveAt(0);

            ChunkCountText.Text = $"Chunks: {_chunkCount:N0}";
            ByteCountText.Text = $"Bytes: {_byteCount:N0}";
            if (!string.IsNullOrWhiteSpace(_trafficLogger.JsonLogPath))
                LogPathText.Text = _trafficLogger.JsonLogPath;
            if (Traffic.Count > 0)
                TrafficGrid.ScrollIntoView(Traffic[^1]);
        }));
    }

    private async Task LogTrafficAsync(string transportName, byte[] data, DateTimeOffset timestamp)
    {
        try
        {
            await _trafficLogger.AppendReceivedAsync(transportName, data, timestamp);
        }
        catch (Exception ex)
        {
            _ = Dispatcher.BeginInvoke(new Action(() => FooterStatusText.Text = $"Decoder logging error: {ex.Message}"));
        }
    }

    private void ClearTraffic_Click(object sender, RoutedEventArgs e)
    {
        Traffic.Clear();
        _chunkCount = 0;
        _byteCount = 0;
        ChunkCountText.Text = "Chunks: 0";
        ByteCountText.Text = "Bytes: 0";
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        var path = _trafficLogger.SessionDirectory;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(ApplicationDataPaths.DataDirectory, "Logs");
            Directory.CreateDirectory(path);
        }
        OpenFolder(path);
    }

    // -----------------------------
    // Helpers / close
    // -----------------------------

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        _closing = true;
        _localBoard?.Dispose();
        StopOnlinePublishing();
        _lapSpeech.Dispose();
        _raceTimer.Stop();
        try { await FinalizeAllPracticeOnCloseAsync(); } catch { }
        try { await CaptureAndSaveSettingsAsync(); } catch { }
        await _connection.DisposeAsync();
        await _decoderIngress.DisposeAsync();
        await _trafficLogger.DisposeAsync();
    }

    private bool IsEditingText()
    {
        if (DashboardRoundCombo.IsDropDownOpen || CurrentRaceCombo.IsDropDownOpen ||
            DashboardRoundCombo.IsKeyboardFocusWithin || CurrentRaceCombo.IsKeyboardFocusWithin) return true;
        var focused = Keyboard.FocusedElement;
        return focused is TextBoxBase or ComboBox || (focused is DataGridCell cell && cell.IsEditing);
    }

    private static int? KeyToDigit(Key key) => key switch
    {
        Key.D0 or Key.NumPad0 => 0,
        Key.D1 or Key.NumPad1 => 1,
        Key.D2 or Key.NumPad2 => 2,
        Key.D3 or Key.NumPad3 => 3,
        Key.D4 or Key.NumPad4 => 4,
        Key.D5 or Key.NumPad5 => 5,
        Key.D6 or Key.NumPad6 => 6,
        Key.D7 or Key.NumPad7 => 7,
        Key.D8 or Key.NumPad8 => 8,
        Key.D9 or Key.NumPad9 => 9,
        _ => null
    };

    private double ParseRaceMinutesOrDefault() =>
        double.TryParse(RaceMinutesText.Text, out var minutes) && minutes > 0 ? minutes : 5;

    private static void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private static string SelectedText(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item)
            return item.Content?.ToString() ?? string.Empty;
        return combo.SelectedItem?.ToString() ?? combo.Text;
    }

    private static string EditableComboText(ComboBox combo) =>
        !string.IsNullOrWhiteSpace(combo.Text) ? combo.Text.Trim() : SelectedText(combo);

    private static void SelectComboByText(ComboBox combo, string text)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private static StopBits ParseStopBits(string value) => value switch
    {
        "1" => StopBits.One,
        "1.5" => StopBits.OnePointFive,
        "2" => StopBits.Two,
        _ => throw new InvalidOperationException("Invalid stop bits setting.")
    };

    private static string FormatHex(ReadOnlySpan<byte> data)
    {
        const int maxShown = 256;
        var shown = data.Length > maxShown ? data[..maxShown] : data;
        var sb = new StringBuilder(shown.Length * 3);
        for (var i = 0; i < shown.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(shown[i].ToString("X2"));
        }
        if (data.Length > maxShown)
            sb.Append($" … (+{data.Length - maxShown} bytes)");
        return sb.ToString();
    }
}
