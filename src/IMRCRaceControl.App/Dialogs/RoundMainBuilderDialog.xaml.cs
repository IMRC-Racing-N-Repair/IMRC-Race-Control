using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using IMRCRaceControl.Models;
using IMRCRaceControl.Core;

namespace IMRCRaceControl.Dialogs;

public partial class RoundMainBuilderDialog : Window
{
    private enum BuildMode
    {
        QualifyingRound,
        Mains
    }

    private sealed record Choice<T>(T Value, string Label);
    private sealed record ClassChoice(string ClassId, string Label, bool AllClasses);

    private sealed record QualifyingRoundScore(
        int Position,
        double Elapsed,
        double BestLap);

    private sealed class QualifyingRankAggregate
    {
        public required string DriverId { get; init; }
        public required string DriverName { get; init; }
        public List<QualifyingRoundScore> Rounds { get; } = [];
    }

    public sealed class SavedSessionRow
    {
        public string SessionId { get; init; } = string.Empty;
        public SessionType SessionType { get; init; }
        public int Round { get; init; }
        public string ClassId { get; init; } = string.Empty;
        public string ClassName { get; init; } = string.Empty;
        public string SessionName { get; init; } = string.Empty;
        public string SessionDisplay { get; init; } = string.Empty;
        public int RaceNumber { get; init; }
        public string Status { get; init; } = string.Empty;
        public int RacerCount { get; init; }
        public string SortDisplay { get; init; } = string.Empty;
        public string DriverSummary { get; init; } = string.Empty;

        public string TypeDisplay => SessionType == SessionType.Qualifying ? "QUAL" : "MAIN";
        public string RoundDisplay => SessionType == SessionType.Qualifying ? $"Q{Round}" : "—";
    }

    public sealed class HeatPlan
    {
        public string SourceSessionId { get; init; } = string.Empty;
        public bool IsLocked { get; init; }
        public string EditStatus => IsLocked ? "LOCKED" : "EDITABLE";
        public int Order { get; set; }
        public int RaceNumber { get; set; }
        public string ClassId { get; init; } = string.Empty;
        public string ClassName { get; init; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SessionDisplay { get; set; } = string.Empty;
        public SessionType SessionType { get; init; }
        public int Round { get; init; }
        public StartStyle StartStyle { get; init; }
        public QualificationOrder QualificationOrder { get; init; }
        public MainSortOrder MainSortOrder { get; init; } = global::IMRCRaceControl.Models.MainSortOrder.QualifyingRank;
        public RaceFormat RaceFormat { get; init; } = RaceFormat.Timed;
        public int TargetLaps { get; init; } = 50;

        public int MainOrder { get; set; }
        public int MainLeg { get; set; } = 1;
        public int MainRunCount { get; set; } = 1;
        public int MainCountBest { get; set; } = 1;
        public int MainCapacity { get; set; }
        public int BumpInCount { get; set; }
        public int BumpUpCount { get; set; }
        public string MainRuleDisplay
        {
            get
            {
                if (SessionType != SessionType.Race)
                    return "—";

                var scoring = MainRunCount > 1
                    ? $"best {MainCountBest}/{MainRunCount} • "
                    : string.Empty;

                var fieldRule = BumpInCount > 0
                    ? $"{Math.Max(0, MainCapacity - BumpInCount)} direct + {BumpInCount} bump / {MainCapacity}"
                    : $"{MainCapacity} max";

                return scoring + fieldRule;
            }
        }

        public ObservableCollection<SessionEntrant> Entrants { get; } = [];
        public int RacerCount => Entrants.Count;
    }

    private readonly RaceDayEventDefinition _raceEvent;
    private readonly IReadOnlyList<ClassDefinition> _classes;
    private readonly IReadOnlyList<DriverProfile> _drivers;
    private bool _loadingOptions;
    private bool _editingExistingScope;
    private readonly HashSet<string> _deletedSessionIds =
        new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<HeatPlan> HeatPlans { get; } = new();
    public ObservableCollection<SavedSessionRow> SavedSessions { get; } = new();

    public IReadOnlyList<EventSessionDefinition> BuiltSessions { get; private set; } = [];
    public IReadOnlyList<string> ReplaceClassIds { get; private set; } = [];
    public IReadOnlyCollection<string> DeletedSessionIds => _deletedSessionIds;
    public bool HasDirectEventChanges => _deletedSessionIds.Count > 0;
    public SessionType BuiltSessionType { get; private set; } = SessionType.Qualifying;
    public int BuiltRound { get; private set; } = 1;

    public RoundMainBuilderDialog(
        RaceDayEventDefinition raceEvent,
        IReadOnlyList<ClassDefinition> classes,
        IReadOnlyList<DriverProfile> drivers,
        string? preferredClassId = null,
        string? editSessionId = null,
        AppSettings? settings = null, bool createNew = false)
    {
        InitializeComponent();
        DataContext = this;

        _raceEvent = raceEvent;
        _classes = classes;
        _drivers = drivers;
        _speechSettings = settings ?? new AppSettings();

        _loadingOptions = true;
        try
        {
            ModeCombo.ItemsSource = new[]
            {
                new Choice<BuildMode>(BuildMode.QualifyingRound, "Qualifying round"),
                new Choice<BuildMode>(BuildMode.Mains, "Mains")
            };
            ModeCombo.DisplayMemberPath = nameof(Choice<BuildMode>.Label);
            ModeCombo.SelectedValuePath = nameof(Choice<BuildMode>.Value);
            ModeCombo.SelectedValue = BuildMode.QualifyingRound;

            var eventClasses = _raceEvent.ClassIds
                .Select(id => _classes.FirstOrDefault(c => c.Id == id))
                .Where(c => c is not null)
                .Cast<ClassDefinition>()
                .ToList();

            var classChoices = new List<ClassChoice>
            {
                new(string.Empty, "ALL EVENT CLASSES", true)
            };
            classChoices.AddRange(eventClasses.Select(c => new ClassChoice(c.Id, c.Name, false)));
            ClassCombo.ItemsSource = classChoices;
            ClassCombo.DisplayMemberPath = nameof(ClassChoice.Label);

            // The normal race-day workflow builds every event class together.
            // Individual classes remain selectable, but the dialog always opens
            // on ALL EVENT CLASSES.
            ClassCombo.SelectedItem = classChoices[0];

            ConfigureSortOptions(BuildMode.QualifyingRound);
            RoundText.Text = "1";
        }
        finally
        {
            _loadingOptions = false;
        }

        Closing += (_, e) =>
        {
            if (!IsEnabled || (DialogResult != true && !ConfirmLeaveLineup())) e.Cancel = true;
        };
        Loaded += (_, _) =>
        {
            UpdateGenerateButtonLabel();
            UpdateModeSpecificControls();
            RefreshSavedSessions();
            InitializeSavedFilters();
            if (!string.IsNullOrEmpty(editSessionId))
            {
                var saved = SavedSessions.FirstOrDefault(s => s.SessionId == editSessionId);
                if (saved is not null)
                {
                    SavedSessionGrid.SelectedItem = saved;
                    LoadSavedSession_Click(this, new RoutedEventArgs());
                    return;
                }
            }
            if (createNew) { Title = "New Round / Mains"; NewRoundButton.Visibility = Visibility.Collapsed; BeginNewRound(); }
            else if (SavedSessions.Count > 0) ShowSavedLineups();
            else { HeatPlans.Clear(); BuildOptionsPanel.Visibility = Visibility.Collapsed; GenerateButton.Visibility = Visibility.Collapsed; Dispatcher.BeginInvoke(new Action(OpenCompactGenerator)); }
        };
    }

    private BuildMode CurrentMode =>
        ModeCombo.SelectedValue is BuildMode mode ? mode : BuildMode.QualifyingRound;

    private QualificationOrder CurrentQualSort =>
        SortCombo.SelectedValue is QualificationOrder sort ? sort : QualificationOrder.Random;

    private MainSortOrder CurrentMainSort =>
        SortCombo.SelectedValue is MainSortOrder sort ? sort : MainSortOrder.QualifyingRank;

    private void ConfigureSortOptions(BuildMode mode)
    {
        if (mode == BuildMode.Mains)
        {
            var sorts = Enum.GetValues<MainSortOrder>()
                .Select(v => new Choice<MainSortOrder>(v, v.ToDisplay()))
                .ToList();

            SortCombo.ItemsSource = sorts;
            SortCombo.DisplayMemberPath = nameof(Choice<MainSortOrder>.Label);
            SortCombo.SelectedValuePath = nameof(Choice<MainSortOrder>.Value);
            SortCombo.SelectedValue = global::IMRCRaceControl.Models.MainSortOrder.QualifyingRank;
        }
        else
        {
            var sorts = Enum.GetValues<QualificationOrder>()
                .Select(v => new Choice<QualificationOrder>(
                    v,
                    v == QualificationOrder.PreviousRound
                        ? "Previous Round Results"
                        : v.ToDisplay()))
                .ToList();

            SortCombo.ItemsSource = sorts;
            SortCombo.DisplayMemberPath = nameof(Choice<QualificationOrder>.Label);
            SortCombo.SelectedValuePath = nameof(Choice<QualificationOrder>.Value);
            SortCombo.SelectedValue = QualificationOrder.Random;
        }
    }

    private IReadOnlyList<ClassDefinition> SelectedClasses()
    {
        if (ClassCombo.SelectedItem is not ClassChoice selected)
            return [];

        if (selected.AllClasses)
            return _raceEvent.ClassIds
                .Select(id => _classes.FirstOrDefault(c => c.Id == id))
                .Where(c => c is not null)
                .Cast<ClassDefinition>()
                .ToList();

        var cls = _classes.FirstOrDefault(c => c.Id == selected.ClassId);
        return cls is null ? [] : [cls];
    }

    private int CurrentRound()
    {
        if (CurrentMode == BuildMode.Mains)
            return 1;
        return int.TryParse(RoundText.Text.Trim(), out var round) && round > 0 ? round : 1;
    }

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingOptions)
            return;

        var mains = CurrentMode == BuildMode.Mains;
        UpdateGenerateButtonLabel();
        UpdateModeSpecificControls();
        RoundPanel.IsEnabled = !mains;
        SortPanel.IsEnabled = true;

        _editingExistingScope = false;

        _loadingOptions = true;
        try
        {
            ConfigureSortOptions(CurrentMode);
        }
        finally
        {
            _loadingOptions = false;
        }

        if (mains)
        {
            BuilderStatusText.Text =
                "Generate the mains, then select A/B/C/etc below to set RUNS and COUNT BEST. Lower mains are auto-sorted first; A Mains are last.";
        }
        else
        {
            ApplyAutomaticNextRound();
        }

        LoadExistingOrGenerate();
    }

    private void BuilderOption_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingOptions)
            return;

        // Changing class/sort means the operator is designing a new preview.
        // Existing saved races remain isolated in the bottom list until
        // LOAD / EDIT SELECTED is explicitly used.
        _editingExistingScope = false;

        if (ReferenceEquals(sender, ClassCombo) &&
            CurrentMode == BuildMode.QualifyingRound)
        {
            ApplyAutomaticNextRound();
        }

        LoadExistingOrGenerate();
    }

    private void BuilderOption_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loadingOptions)
            LoadExistingOrGenerate();
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (HasLockedPlans) { ShowLockedStructureMessage(); return; }
        _editingExistingScope = false;
        GenerateFromRoster(force: true);
    }

    private void UpdateGenerateButtonLabel()
    {
        if (GenerateButton is null)
            return;

        GenerateButton.Content = CurrentMode == BuildMode.Mains
            ? "GENERATE MAINS"
            : "GENERATE ROUND";
    }

    private void UpdateModeSpecificControls()
    {
        var mains = CurrentMode == BuildMode.Mains;

        if (MainSeriesSettingsPanel is not null)
            MainSeriesSettingsPanel.Visibility = mains ? Visibility.Visible : Visibility.Collapsed;

        if (AutoSortMainsButton is not null)
            AutoSortMainsButton.Visibility = mains ? Visibility.Visible : Visibility.Collapsed;

        if (HeatUpButton is not null)
            HeatUpButton.Content = mains ? "RACE UP" : "HEAT UP";

        if (HeatDownButton is not null)
            HeatDownButton.Content = mains ? "RACE DOWN" : "HEAT DOWN";

        if (AddHeatButton is not null)
            AddHeatButton.Content = mains ? "+ EMPTY LOWER MAIN" : "+ EMPTY HEAT";

        RefreshSelectedMainSeriesEditor(HeatGrid?.SelectedItem as HeatPlan);
    }

    private void LoadSavedSession_Click(object sender, RoutedEventArgs e)
    {
        if (SavedSessionGrid.SelectedItem is not SavedSessionRow saved)
        {
            MessageBox.Show(this,
                "Select a saved heat/main first.",
                "Saved rounds / mains",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ConfirmLeaveLineup()) return;

        var classChoice = (ClassCombo.ItemsSource as IEnumerable<ClassChoice>)?
            .FirstOrDefault(c => !c.AllClasses &&
                                 string.Equals(c.ClassId, saved.ClassId, StringComparison.OrdinalIgnoreCase));

        _loadingOptions = true;
        try
        {
            var targetMode = saved.SessionType == SessionType.Qualifying
                ? BuildMode.QualifyingRound
                : BuildMode.Mains;
            ModeCombo.SelectedValue = targetMode;
            ConfigureSortOptions(targetMode);

            if (classChoice is not null)
                ClassCombo.SelectedItem = classChoice;

            var source = _raceEvent.Sessions.FirstOrDefault(s => s.Id == saved.SessionId);
            if (saved.SessionType == SessionType.Qualifying)
            {
                RoundText.Text = saved.Round.ToString();
                if (source is not null)
                    SortCombo.SelectedValue = source.QualificationOrder;
            }
            else if (source is not null)
            {
                SortCombo.SelectedValue = source.MainSortOrder;
            }
        }
        finally
        {
            _loadingOptions = false;
        }

        _browsingSavedLineups = false;
        _editingExistingScope = true;
        SyncSavedFilters(saved);
        var mains = CurrentMode == BuildMode.Mains;
        RoundPanel.IsEnabled = !mains;
        SortPanel.IsEnabled = false;
        ClassCombo.IsEnabled = false;
        ModeCombo.IsEnabled = false;
        GenerateButton.IsEnabled = false;
        BuildOptionsPanel.Visibility = Visibility.Collapsed;
        GenerateButton.Visibility = Visibility.Collapsed;
        SaveButton.Content = "SAVE CHANGES";
        UpdateGenerateButtonLabel();
        UpdateModeSpecificControls();
        LoadExistingOrGenerate();
        HeatGrid.SelectedItem = HeatPlans.FirstOrDefault(h => h.SourceSessionId == saved.SessionId)
            ?? HeatPlans.FirstOrDefault();
        RememberLineup();
    }

    private void DeleteSavedSession_Click(object sender, RoutedEventArgs e)
    {
        if (SavedSessionGrid.SelectedItem is not SavedSessionRow saved)
        {
            MessageBox.Show(this,
                "Select a saved race first.",
                "Delete race",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var session = _raceEvent.Sessions.FirstOrDefault(s =>
            string.Equals(s.Id, saved.SessionId, StringComparison.OrdinalIgnoreCase));

        if (session is null)
        {
            RefreshSavedSessions();
            return;
        }

        if (session.ProgressStatus == EventSessionStatus.Running)
        {
            MessageBox.Show(this,
                "A race marked RUNNING cannot be deleted from the builder.",
                "Delete race",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var warning = session.ProgressStatus == EventSessionStatus.Completed
            ? $"Delete {session.Name}?\n\nThis race has completed results. Its laps, result, and contribution to event rankings will be removed."
            : $"Delete {session.Name}?\n\nThis removes the saved race from the event.";

        if (MessageBox.Show(this,
                warning,
                "Delete saved race",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _raceEvent.Sessions.Remove(session);
        _deletedSessionIds.Add(session.Id);

        if (_editingExistingScope)
            RemoveMatchingPreviewPlan(session);

        NormalizeSavedMainSeriesAfterDelete(session);
        NormalizeSavedQueueNumbers();

        RefreshSavedSessions();
        HeatGrid.Items.Refresh();
        RefreshTargetHeats();

        if (_editingExistingScope)
        {
            // A loaded/editing scope should immediately reflect the remaining
            // saved races and unlock itself once its completed races are gone.
            LoadExistingOrGenerate();
        }
        else
        {
            SaveButton.IsEnabled = HeatPlans.Count > 0;
            BuilderStatusText.Text =
                $"{session.Name} deleted from the saved event. The preview above is unchanged.";
        }
    }

    private void RemoveMatchingPreviewPlan(EventSessionDefinition session)
    {
        var match = HeatPlans.FirstOrDefault(h =>
            h.SessionType == session.SessionType &&
            string.Equals(h.ClassId, session.ClassId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(h.Name, session.Name, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return;

        HeatPlans.Remove(match);

        if (session.SessionType == SessionType.Race)
        {
            var siblings = HeatPlans
                .Where(h =>
                    h.SessionType == SessionType.Race &&
                    string.Equals(h.ClassId, session.ClassId, StringComparison.OrdinalIgnoreCase) &&
                    h.MainOrder == session.Heat)
                .OrderBy(h => h.MainLeg)
                .ToList();

            if (siblings.Count > 0)
            {
                var countBest = Math.Clamp(
                    siblings[0].MainCountBest,
                    1,
                    siblings.Count);

                for (var i = 0; i < siblings.Count; i++)
                {
                    siblings[i].MainLeg = i + 1;
                    siblings[i].MainRunCount = siblings.Count;
                    siblings[i].MainCountBest = countBest;
                }
            }
        }

        ReorderNumbers();
        HeatGrid.SelectedIndex = HeatPlans.Count > 0 ? 0 : -1;
        RefreshSelectedMainSeriesEditor(HeatGrid.SelectedItem as HeatPlan);
    }

    private void NormalizeSavedMainSeriesAfterDelete(EventSessionDefinition deleted)
    {
        if (deleted.SessionType != SessionType.Race)
        {
            RefreshSavedBumpTargets();
            return;
        }

        var siblings = _raceEvent.Sessions
            .Where(s =>
                s.SessionType == SessionType.Race &&
                string.Equals(s.ClassId, deleted.ClassId, StringComparison.OrdinalIgnoreCase) &&
                s.Heat == deleted.Heat)
            .OrderBy(s => s.MainLeg)
            .ThenBy(s => s.QueueOrder)
            .ToList();

        if (siblings.Count > 0)
        {
            var countBest = Math.Clamp(
                siblings[0].MainCountBest,
                1,
                siblings.Count);

            for (var i = 0; i < siblings.Count; i++)
            {
                var session = siblings[i];
                session.MainLeg = i + 1;
                session.MainRunCount = siblings.Count;
                session.MainCountBest = countBest;

                var mainLabel = MainLetter(Math.Max(0, session.Heat - 1));
                session.Name = siblings.Count > 1
                    ? $"{session.ClassName} {mainLabel} Main {i + 1}"
                    : $"{session.ClassName} {mainLabel} Main";
            }
        }

        RefreshSavedBumpTargets();
    }

    private void RefreshSavedBumpTargets()
    {
        foreach (var lower in _raceEvent.Sessions.Where(s =>
                     s.SessionType == SessionType.Race &&
                     s.BumpUpCount > 0))
        {
            var upper = _raceEvent.Sessions
                .Where(s =>
                    s.SessionType == SessionType.Race &&
                    string.Equals(s.ClassId, lower.ClassId, StringComparison.OrdinalIgnoreCase) &&
                    s.Heat == lower.Heat - 1)
                .OrderBy(s => s.MainLeg)
                .FirstOrDefault();

            lower.BumpTargetSessionId = upper?.Id ?? string.Empty;
        }
    }

    private void NormalizeSavedQueueNumbers()
    {
        var order = 1;
        foreach (var session in _raceEvent.Sessions
                     .OrderBy(s => s.QueueOrder > 0 ? s.QueueOrder : int.MaxValue)
                     .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            session.QueueOrder = order++;
        }
    }

    private void ApplyAutomaticNextRound()
    {
        if (CurrentMode != BuildMode.QualifyingRound)
            return;

        var classIds = SelectedClasses()
            .Select(c => c.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var highestSavedRound = _raceEvent.Sessions
            .Where(s => s.SessionType == SessionType.Qualifying &&
                        classIds.Contains(s.ClassId))
            .Select(s => s.Round)
            .DefaultIfEmpty(0)
            .Max();

        RoundText.Text = Math.Max(1, highestSavedRound + 1).ToString();
        UpdateNextRoundGuidance();
    }

    private void UpdateNextRoundGuidance()
    {
        if (CurrentMode != BuildMode.QualifyingRound)
            return;

        var round = CurrentRound();
        if (round <= 1)
        {
            BuilderStatusText.Text =
                "Q1 is the first saved round. After it is saved, the builder automatically advances to Q2.";
            return;
        }

        var selectedClassIds = SelectedClasses()
            .Select(c => c.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var previous = _raceEvent.Sessions
            .Where(s => s.SessionType == SessionType.Qualifying &&
                        s.Round == round - 1 &&
                        selectedClassIds.Contains(s.ClassId))
            .ToList();

        if (previous.Count > 0 &&
            previous.All(s => s.ProgressStatus == EventSessionStatus.Completed))
        {
            BuilderStatusText.Text =
                $"Q{round} is next. Q{round - 1} is complete, so 'Previous Round Results' can seed this round from actual results.";
        }
        else
        {
            BuilderStatusText.Text =
                $"Q{round} is next. Q{round - 1} remains saved. 'Previous Round Results' becomes available after all Q{round - 1} heats are complete.";
        }
    }

    private void RefreshSavedSessions()
    {
        RaceSchedule.AssignRaceNumbers(_raceEvent.Sessions);
        SavedSessions.Clear();

        foreach (var session in _raceEvent.Sessions
                     .OrderBy(s => s.QueueOrder > 0 ? s.QueueOrder : int.MaxValue)
                     .ThenBy(s => s.SessionType == SessionType.Qualifying ? 0 : 1)
                     .ThenBy(s => s.Round)
                     .ThenBy(s => EventClassOrder(s.ClassId))
                     .ThenBy(s => s.SessionType == SessionType.Race ? -s.Heat : s.Heat)
                     .ThenBy(s => s.MainLeg))
        {
            SavedSessions.Add(new SavedSessionRow
            {
                SessionId = session.Id,
                SessionType = session.SessionType,
                Round = session.Round,
                ClassId = session.ClassId,
                ClassName = session.ClassName,
                SessionName = session.Name,
                SessionDisplay = BuildCompactSessionDisplay(session),
                RaceNumber = session.RaceNumber,
                Status = session.StatusDisplay,
                RacerCount = session.RacerCount,
                SortDisplay = session.SessionType == SessionType.Qualifying
                    ? (session.QualificationOrder == QualificationOrder.PreviousRound
                        ? "Previous Results"
                        : session.QualificationOrderDisplay)
                    : session.MainSortOrder.ToDisplay(),
                DriverSummary = BuildSavedSessionSummary(session)
            });
        }

        SavedSessionGrid?.Items.Refresh();
    }

    private string BuildCompactSessionDisplay(EventSessionDefinition session)
    {
        if (session.SessionType == SessionType.Qualifying)
        {
            var heats = _raceEvent.Sessions
                .Where(s =>
                    s.SessionType == SessionType.Qualifying &&
                    string.Equals(s.ClassId, session.ClassId, StringComparison.OrdinalIgnoreCase) &&
                    s.Round == session.Round)
                .OrderBy(s => s.Heat)
                .ThenBy(s => s.QueueOrder)
                .ToList();

            if (heats.Count <= 1)
                return "Qual";

            var heatIndex = heats.FindIndex(s =>
                string.Equals(s.Id, session.Id, StringComparison.OrdinalIgnoreCase));

            return $"Qual • Heat {Math.Max(1, heatIndex + 1)}/{heats.Count}";
        }

        var mainLabel = MainLetter(Math.Max(0, session.Heat - 1));

        return session.MainRunCount > 1
            ? $"{mainLabel}-Main {Math.Max(1, session.MainLeg)}/{Math.Max(1, session.MainRunCount)}"
            : $"{mainLabel}-Main";
    }

    private int EventClassOrder(string classId)
    {
        for (var i = 0; i < _raceEvent.ClassIds.Count; i++)
        {
            if (string.Equals(_raceEvent.ClassIds[i], classId, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return int.MaxValue;
    }

    private static string BuildSavedSessionSummary(EventSessionDefinition session)
    {
        if (session.ProgressStatus == EventSessionStatus.Completed && session.Results.Count > 0)
        {
            var winner = session.Results
                .OrderBy(r => r.Position)
                .First();

            return $"1st: {winner.DriverName} • {winner.Laps} laps • Best {winner.BestLap}";
        }

        var names = session.Entrants
            .OrderBy(e => e.CarNumber)
            .Select(e => e.DisplayName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        return names.Count == 0
            ? "No drivers"
            : string.Join(", ", names);
    }

    private void LoadExistingOrGenerate()
    {
        if (!IsLoaded && !HeatPlans.Any())
            return;

        RefreshSavedSessions();

        var classes = SelectedClasses();
        if (classes.Count == 0)
        {
            HeatPlans.Clear();
            BuilderStatusText.Text = "Add at least one class to the event first.";
            return;
        }

        var round = CurrentRound();
        if (CurrentMode == BuildMode.QualifyingRound && (round < 1 || round > 99))
        {
            BuilderStatusText.Text = "Round must be from 1 to 99.";
            return;
        }

        // The top half is a BUILD PREVIEW. Saved/raced sessions live only in
        // the bottom list unless the operator explicitly chooses LOAD / EDIT.
        if (!_editingExistingScope)
        {
            SaveButton.IsEnabled = true;
            GenerateFromRoster(force: true);

            if (CurrentMode == BuildMode.Mains)
            {
                BuilderStatusText.Text =
                    $"Previewing {HeatPlans.Count} main race(s). Existing saved/ran races remain listed below until SAVE / REBUILD.";
            }
            else
            {
                BuilderStatusText.Text =
                    $"Previewing Q{round}: {HeatPlans.Count} heat(s). Existing saved/ran races remain listed below.";
            }

            return;
        }

        var classIds = classes
            .Select(c => c.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existing = _raceEvent.Sessions
            .Where(s => classIds.Contains(s.ClassId) &&
                        ((CurrentMode == BuildMode.QualifyingRound &&
                          s.SessionType == SessionType.Qualifying &&
                          s.Round == round) ||
                         (CurrentMode == BuildMode.Mains &&
                          s.SessionType == SessionType.Race)))
            .OrderBy(s => s.QueueOrder)
            .ThenBy(s => s.ClassName)
            .ThenBy(s => s.SessionType == SessionType.Race ? -s.Heat : s.Heat)
            .ThenBy(s => s.MainLeg)
            .ToList();

        if (existing.Count == 0)
        {
            _editingExistingScope = false;
            GenerateFromRoster(force: true);
            BuilderStatusText.Text = "That saved scope no longer exists. A new preview was generated instead.";
            return;
        }

        LoadExisting(existing);

        if (existing.All(s => s.ProgressStatus == EventSessionStatus.Completed))
        {
            SaveButton.IsEnabled = false;
            BuilderStatusText.Text =
                "All races in this selection are completed and locked. Select another saved round to edit unrun races.";
            return;
        }

        SaveButton.IsEnabled = true;
        BuilderStatusText.Text =
            CurrentMode == BuildMode.QualifyingRound
                ? $"Editing saved Q{round}. Add/remove racers or adjust lineups, then SAVE CHANGES. Completed heats stay locked."
                : $"Editing {existing.Count} saved main race(s). SAVE CHANGES updates this class; completed mains stay locked.";
    }

    private void LoadExisting(IReadOnlyList<EventSessionDefinition> sessions)
    {
        HeatPlans.Clear();
        foreach (var session in sessions
                     .OrderBy(s => _browsingSavedLineups ? s.QueueOrder : s.SessionType == SessionType.Race ? -s.Heat : s.QueueOrder)
                     .ThenBy(s => s.SessionType == SessionType.Race ? s.MainLeg : s.Heat)
                     .ThenBy(s => s.QueueOrder))
        {
            var plan = new HeatPlan
            {
                SourceSessionId = session.Id,
                IsLocked = session.ProgressStatus == EventSessionStatus.Completed,
                Order = HeatPlans.Count + 1,
                RaceNumber = session.RaceNumber,
                ClassId = session.ClassId,
                ClassName = session.ClassName,
                Name = session.Name,
                SessionType = session.SessionType,
                Round = session.Round,
                StartStyle = session.StartStyle,
                QualificationOrder = session.QualificationOrder,
                MainSortOrder = session.MainSortOrder,
                RaceFormat = session.RaceFormat,
                TargetLaps = session.TargetLaps,
                MainOrder = session.SessionType == SessionType.Race ? Math.Max(1, session.Heat) : 0,
                MainLeg = Math.Max(1, session.MainLeg),
                MainRunCount = Math.Max(1, session.MainRunCount),
                MainCountBest = Math.Clamp(session.MainCountBest, 1, Math.Max(1, session.MainRunCount)),
                MainCapacity = session.MainCapacity,
                BumpInCount = session.BumpInCount,
                BumpUpCount = session.BumpUpCount
            };

            foreach (var entrant in session.Entrants.OrderBy(e => e.CarNumber))
                plan.Entrants.Add(CloneEntrant(entrant));

            HeatPlans.Add(plan);
        }

        foreach (var classId in HeatPlans
                     .Select(h => h.ClassId)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            RenameClassPlans(classId);
        }

        RefreshPreviewRaceNumbers();
        HeatGrid.Items.Refresh();
        HeatGrid.SelectedIndex = HeatPlans.Count > 0 ? 0 : -1;
        RefreshSelectedMainSeriesEditor(HeatGrid.SelectedItem as HeatPlan);
    }

    private void GenerateFromRoster(bool force)
    {
        try
        {
            var classes = SelectedClasses();
            if (classes.Count == 0)
                throw new InvalidOperationException("Add and select at least one event class.");

            var round = CurrentRound();
            if (CurrentMode == BuildMode.QualifyingRound && (round < 1 || round > 99))
                throw new InvalidOperationException("Round must be from 1 to 99.");

            if (!force && HeatPlans.Count > 0)
                return;

            HeatPlans.Clear();
            foreach (var cls in classes)
            {
                var entrants = BuildEntrants(cls.Id);
                if (entrants.Count == 0)
                    continue;

                if (CurrentMode == BuildMode.QualifyingRound)
                    entrants = SortQualifying(cls.Id, entrants, round, CurrentQualSort);
                else
                    entrants = SortForMains(cls.Id, entrants, CurrentMainSort);

                if (CurrentMode == BuildMode.QualifyingRound)
                {
                    var target = Math.Clamp(cls.MaxRacersPerHeat, 1, 30);
                    var count = Math.Max(1, (int)Math.Ceiling(entrants.Count / (double)target));

                    for (var heat = 0; heat < count; heat++)
                    {
                        var group = entrants.Skip(heat * target).Take(target).Select(CloneEntrant).ToList();
                        Renumber(group);

                        var plan = new HeatPlan
                        {
                            Order = HeatPlans.Count + 1,
                            ClassId = cls.Id,
                            ClassName = cls.Name,
                            SessionType = SessionType.Qualifying,
                            Round = round,
                            StartStyle = cls.QualStartStyle,
                            QualificationOrder = CurrentQualSort,
                            RaceFormat = cls.FormatFor(SessionType.Qualifying),
                            TargetLaps = cls.LapsFor(SessionType.Qualifying)
                        };

                        foreach (var entrant in group)
                            plan.Entrants.Add(entrant);

                        HeatPlans.Add(plan);
                    }
                }
                else
                {
                    GenerateMainsForClass(cls, entrants);
                }

                RenameClassPlans(cls.Id);
                RefreshMainPlanLinks(cls.Id);
            }

            if (CurrentMode == BuildMode.Mains)
                AutoSortMainPlans();
            else
                ReorderNumbers();

            HeatGrid.Items.Refresh();
            HeatGrid.SelectedIndex = HeatPlans.Count > 0 ? 0 : -1;
            SaveButton.IsEnabled = HeatPlans.Count > 0;
            BuilderStatusText.Text = HeatPlans.Count == 0
                ? "No drivers are checked into the selected class scope."
                : CurrentMode == BuildMode.QualifyingRound
                    ? $"Generated Q{round}: {HeatPlans.Count} heat(s). Earlier qualifying rounds remain saved and are shown above."
                    : $"Generated {HeatPlans.Count} main race(s), automatically ordered from the lowest main up to A Main. Select any main below to change RUNS / COUNT BEST.";
        }
        catch (Exception ex)
        {
            HeatPlans.Clear();
            BuilderStatusText.Text = ex.Message;
            SaveButton.IsEnabled = false;
        }
    }

    private void GenerateMainsForClass(
        ClassDefinition cls,
        IReadOnlyList<SessionEntrant> rankedEntrants)
    {
        var rules = (cls.MainRules ?? [])
            .OrderBy(r => r.Order)
            .Select(r => r.Clone())
            .ToList();

        if (rules.Count == 0)
        {
            rules.Add(new MainRuleDefinition
            {
                Order = 1,
                MaxRacers = Math.Clamp(cls.MaxRacersPerMain, 1, 30),
                BumpInsFromLower = 0,
                RunCount = 1,
                CountBest = 1
            });
        }

        foreach (var rule in rules)
        {
            rule.RunCount = Math.Clamp(rule.RunCount, 1, 10);
            rule.CountBest = Math.Clamp(rule.CountBest, 1, rule.RunCount);
        }

        // Add automatic lower-main rules only if the configured list cannot hold
        // the complete field. This prevents a large class roster from losing
        // racers while still honoring explicitly configured A/B/C/etc rules.
        while (MainFieldCapacity(rules, rules.Count) < rankedEntrants.Count)
        {
            rules.Add(new MainRuleDefinition
            {
                Order = rules.Count + 1,
                MaxRacers = Math.Clamp(cls.MaxRacersPerMain, 1, 30),
                BumpInsFromLower = 0,
                RunCount = 1,
                CountBest = 1
            });

            if (rules.Count >= 26)
                break;
        }

        var usedCount = 1;
        for (; usedCount <= rules.Count; usedCount++)
        {
            if (MainFieldCapacity(rules, usedCount) >= rankedEntrants.Count)
                break;
        }

        usedCount = Math.Min(usedCount, rules.Count);
        var usedRules = rules.Take(usedCount).ToList();

        var entrantIndex = 0;
        for (var mainIndex = 0; mainIndex < usedRules.Count; mainIndex++)
        {
            var rule = usedRules[mainIndex];
            var mainOrder = mainIndex + 1;
            var hasLowerMain = mainIndex < usedRules.Count - 1;
            var bumpIn = hasLowerMain
                ? Math.Clamp(rule.BumpInsFromLower, 0, Math.Max(0, rule.MaxRacers - 1))
                : 0;

            var directSlots = hasLowerMain
                ? Math.Max(1, rule.MaxRacers - bumpIn)
                : rule.MaxRacers;

            var directQualifiers = rankedEntrants
                .Skip(entrantIndex)
                .Take(directSlots)
                .Select(CloneEntrant)
                .ToList();

            entrantIndex += directQualifiers.Count;
            Renumber(directQualifiers);

            for (var leg = 1; leg <= rule.RunCount; leg++)
            {
                var plan = new HeatPlan
                {
                    Order = HeatPlans.Count + 1,
                    ClassId = cls.Id,
                    ClassName = cls.Name,
                    SessionType = SessionType.Race,
                    Round = 1,
                    StartStyle = cls.RaceStartStyle,
                    QualificationOrder = QualificationOrder.Ranked,
                    MainSortOrder = CurrentMainSort,
                    RaceFormat = cls.FormatFor(SessionType.Race),
                    TargetLaps = cls.LapsFor(SessionType.Race),
                    MainOrder = mainOrder,
                    MainLeg = leg,
                    MainRunCount = rule.RunCount,
                    MainCountBest = rule.CountBest,
                    MainCapacity = rule.MaxRacers,
                    BumpInCount = bumpIn
                };

                foreach (var entrant in directQualifiers.Select(CloneEntrant))
                    plan.Entrants.Add(entrant);

                Renumber(plan.Entrants);
                HeatPlans.Add(plan);
            }
        }

        // Safety fallback for an exceptionally large field after the configured
        // A-Z rule list is exhausted. These extra lower mains use one run each.
        if (entrantIndex < rankedEntrants.Count)
        {
            var remaining = rankedEntrants.Skip(entrantIndex).Select(CloneEntrant).ToList();
            var nextMainOrder = usedRules.Count + 1;

            foreach (var chunk in remaining.Chunk(Math.Clamp(cls.MaxRacersPerMain, 1, 30)))
            {
                var group = chunk.ToList();
                Renumber(group);

                var plan = new HeatPlan
                {
                    Order = HeatPlans.Count + 1,
                    ClassId = cls.Id,
                    ClassName = cls.Name,
                    SessionType = SessionType.Race,
                    Round = 1,
                    StartStyle = cls.RaceStartStyle,
                    QualificationOrder = QualificationOrder.Ranked,
                    MainSortOrder = CurrentMainSort,
                    RaceFormat = cls.FormatFor(SessionType.Race),
                    TargetLaps = cls.LapsFor(SessionType.Race),
                    MainOrder = nextMainOrder++,
                    MainLeg = 1,
                    MainRunCount = 1,
                    MainCountBest = 1,
                    MainCapacity = Math.Clamp(cls.MaxRacersPerMain, 1, 30),
                    BumpInCount = 0
                };

                foreach (var entrant in group)
                    plan.Entrants.Add(entrant);

                HeatPlans.Add(plan);
            }
        }
    }

    private static int MainFieldCapacity(
        IReadOnlyList<MainRuleDefinition> rules,
        int usedCount)
    {
        if (rules.Count == 0 || usedCount <= 0)
            return 0;

        usedCount = Math.Min(usedCount, rules.Count);
        var capacity = 0;

        for (var i = 0; i < usedCount; i++)
        {
            var rule = rules[i];
            var max = Math.Clamp(rule.MaxRacers, 1, 30);
            var hasLower = i < usedCount - 1;

            capacity += hasLower
                ? Math.Max(1, max - Math.Clamp(rule.BumpInsFromLower, 0, Math.Max(0, max - 1)))
                : max;
        }

        return capacity;
    }

    private void RefreshMainPlanLinks(string classId)
    {
        var mainGroups = HeatPlans
            .Where(h => h.ClassId == classId && h.SessionType == SessionType.Race)
            .GroupBy(h => h.MainOrder)
            .OrderBy(g => g.Key)
            .ToList();

        for (var groupIndex = 0; groupIndex < mainGroups.Count; groupIndex++)
        {
            var group = mainGroups[groupIndex].OrderBy(h => h.MainLeg).ToList();
            var bumpUpCount = groupIndex == 0
                ? 0
                : Math.Clamp(
                    mainGroups[groupIndex - 1].First().BumpInCount,
                    0,
                    Math.Max(0, mainGroups[groupIndex - 1].First().MainCapacity - 1));

            foreach (var plan in group)
                plan.BumpUpCount = bumpUpCount;
        }
    }

    private List<SessionEntrant> BuildEntrants(string classId)
    {
        var result = new List<SessionEntrant>();
        foreach (var assignment in _raceEvent.DriverAssignments
                     .Where(a => a.ClassId == classId)
                     .GroupBy(a => a.DriverId, StringComparer.OrdinalIgnoreCase)
                     .Select(g => g.First()))
        {
            var driver = _drivers.FirstOrDefault(d => d.Id == assignment.DriverId);
            if (driver is null)
                continue;

            result.Add(new SessionEntrant
            {
                DriverId = driver.Id,
                DriverName = driver.FullName,
                Nickname = driver.Nickname,
                TransponderId = assignment.TransponderId,
                SeedRank = assignment.SeedRank
            });
        }

        return result;
    }

    private List<SessionEntrant> SortQualifying(
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
                .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            QualificationOrder.Manual => entrants
                .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            QualificationOrder.PreviousRound => SortByPreviousRound(classId, entrants, round),
            _ => entrants
        };
    }

    private List<SessionEntrant> SortByPreviousRound(
        string classId,
        List<SessionEntrant> entrants,
        int round)
    {
        if (round <= 1)
            throw new InvalidOperationException("Previous Round sorting requires Q2 or later.");

        var prior = _raceEvent.Sessions
            .Where(s => s.ClassId == classId &&
                        s.SessionType == SessionType.Qualifying &&
                        s.Round == round - 1)
            .ToList();

        if (prior.Count == 0 || prior.Any(s => s.ProgressStatus != EventSessionStatus.Completed))
            throw new InvalidOperationException($"Complete all Q{round - 1} heats before using Previous Round sorting.");

        var order = GetCompletedRoundOrder(prior)
            .Select((driverId, index) => new { driverId, index })
            .ToDictionary(x => x.driverId, x => x.index, StringComparer.OrdinalIgnoreCase);

        return entrants
            .OrderBy(e => order.TryGetValue(e.DriverId, out var index) ? index : int.MaxValue)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<SessionEntrant> SortForMains(
        string classId,
        List<SessionEntrant> entrants,
        MainSortOrder sortOrder)
    {
        if (sortOrder == MainSortOrder.Random)
            return entrants.OrderBy(_ => Random.Shared.Next()).ToList();

        if (sortOrder == MainSortOrder.DriverSeed)
        {
            return entrants
                .OrderBy(e => e.SeedRank <= 0 ? int.MaxValue : e.SeedRank)
                .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (sortOrder == MainSortOrder.RegistrationOrder)
        {
            var registrationOrder = _raceEvent.DriverAssignments
                .Where(a => string.Equals(a.ClassId, classId, StringComparison.OrdinalIgnoreCase))
                .Select((a, index) => new { a.DriverId, index })
                .GroupBy(x => x.DriverId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Min(x => x.index), StringComparer.OrdinalIgnoreCase);

            return entrants
                .OrderBy(e => registrationOrder.TryGetValue(e.DriverId, out var index) ? index : int.MaxValue)
                .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Default / IFMAR-style main seeding: calculate directly from the
        // qualifying sessions that still exist in this event. This means a
        // race deleted from the bottom list is immediately removed from main
        // seeding without requiring the builder to close/reopen first.
        var qualifyingOrder = GetCurrentQualifyingRankOrder(classId)
            .Select((driverId, index) => new { driverId, index })
            .ToDictionary(x => x.driverId, x => x.index, StringComparer.OrdinalIgnoreCase);

        return entrants
            .OrderBy(e => qualifyingOrder.TryGetValue(e.DriverId, out var index) ? index : int.MaxValue)
            .ThenBy(e => e.SeedRank <= 0 ? int.MaxValue : e.SeedRank)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<string> GetCurrentQualifyingRankOrder(string classId)
    {
        var completed = _raceEvent.Sessions
            .Where(s =>
                s.SessionType == SessionType.Qualifying &&
                string.Equals(s.ClassId, classId, StringComparison.OrdinalIgnoreCase) &&
                s.ProgressStatus == EventSessionStatus.Completed &&
                s.Results.Count > 0)
            .ToList();

        if (completed.Count == 0)
            return [];

        var aggregates = new Dictionary<string, QualifyingRankAggregate>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var roundGroup in completed.GroupBy(s => s.Round).OrderBy(g => g.Key))
        {
            var performances = new List<(
                string DriverId,
                string DriverName,
                int Laps,
                double Elapsed,
                double BestLap)>();

            foreach (var session in roundGroup)
            {
                foreach (var result in session.Results)
                {
                    var entrant = session.Entrants.FirstOrDefault(e =>
                        e.CarNumber == result.CarNumber);

                    if (entrant is null || string.IsNullOrWhiteSpace(entrant.DriverId))
                        continue;

                    var laps = session.Laps
                        .Where(l => l.CarNumber == result.CarNumber)
                        .OrderBy(l => l.LapNumber)
                        .ToList();

                    performances.Add((
                        entrant.DriverId,
                        entrant.DisplayName,
                        laps.Count,
                        laps.Count == 0 ? double.MaxValue : laps[^1].Elapsed.TotalSeconds,
                        laps.Count == 0 ? double.MaxValue : laps.Min(l => l.LapTime.TotalSeconds)));
                }
            }

            var ordered = performances
                .GroupBy(p => p.DriverId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g
                    .OrderByDescending(x => x.Laps)
                    .ThenBy(x => x.Elapsed)
                    .ThenBy(x => x.BestLap)
                    .First())
                .OrderByDescending(x => x.Laps)
                .ThenBy(x => x.Elapsed)
                .ThenBy(x => x.BestLap)
                .ThenBy(x => x.DriverName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                var performance = ordered[i];

                if (!aggregates.TryGetValue(performance.DriverId, out var aggregate))
                {
                    aggregate = new QualifyingRankAggregate
                    {
                        DriverId = performance.DriverId,
                        DriverName = performance.DriverName
                    };

                    aggregates.Add(performance.DriverId, aggregate);
                }

                aggregate.Rounds.Add(new QualifyingRoundScore(
                    i + 1,
                    performance.Elapsed,
                    performance.BestLap));
            }
        }

        var ranked = aggregates.Values.ToList();
        ranked.Sort(CompareQualifyingRankAggregate);

        return ranked
            .Select(aggregate => aggregate.DriverId)
            .ToList();
    }

    private static int CompareQualifyingRankAggregate(
        QualifyingRankAggregate left,
        QualifyingRankAggregate right)
    {
        var leftPositions = left.Rounds
            .Select(r => r.Position)
            .OrderBy(position => position)
            .ToList();

        var rightPositions = right.Rounds
            .Select(r => r.Position)
            .OrderBy(position => position)
            .ToList();

        var count = Math.Max(leftPositions.Count, rightPositions.Count);

        for (var i = 0; i < count; i++)
        {
            var leftPosition = i < leftPositions.Count
                ? leftPositions[i]
                : int.MaxValue;

            var rightPosition = i < rightPositions.Count
                ? rightPositions[i]
                : int.MaxValue;

            var compare = leftPosition.CompareTo(rightPosition);
            if (compare != 0)
                return compare;
        }

        var leftBest = left.Rounds
            .OrderBy(r => r.Position)
            .ThenBy(r => r.Elapsed)
            .ThenBy(r => r.BestLap)
            .First();

        var rightBest = right.Rounds
            .OrderBy(r => r.Position)
            .ThenBy(r => r.Elapsed)
            .ThenBy(r => r.BestLap)
            .First();

        var elapsedCompare = leftBest.Elapsed.CompareTo(rightBest.Elapsed);
        if (elapsedCompare != 0)
            return elapsedCompare;

        var bestLapCompare = leftBest.BestLap.CompareTo(rightBest.BestLap);
        if (bestLapCompare != 0)
            return bestLapCompare;

        return string.Compare(
            left.DriverName,
            right.DriverName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> GetCompletedRoundOrder(IReadOnlyList<EventSessionDefinition> sessions)
    {
        var performances = new List<(string DriverId, string DriverName, int Laps, double Elapsed, double BestLap)>();

        foreach (var session in sessions)
        {
            foreach (var result in session.Results)
            {
                var entrant = session.Entrants.FirstOrDefault(e => e.CarNumber == result.CarNumber);
                if (entrant is null)
                    continue;

                var laps = session.Laps.Where(l => l.CarNumber == result.CarNumber).OrderBy(l => l.LapNumber).ToList();
                performances.Add((
                    entrant.DriverId,
                    entrant.DisplayName,
                    laps.Count,
                    laps.Count == 0 ? double.MaxValue : laps[^1].Elapsed.TotalSeconds,
                    laps.Count == 0 ? double.MaxValue : laps.Min(l => l.LapTime.TotalSeconds)));
            }
        }

        return performances
            .GroupBy(p => p.DriverId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Laps).ThenBy(x => x.Elapsed).ThenBy(x => x.BestLap).First())
            .OrderByDescending(x => x.Laps)
            .ThenBy(x => x.Elapsed)
            .ThenBy(x => x.BestLap)
            .ThenBy(x => x.DriverName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.DriverId)
            .ToList();
    }

    private void HeatGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = HeatGrid.SelectedItem as HeatPlan;
        if (_browsingSavedLineups && !_loadingSavedFilters && IsLoaded && selected is { SourceSessionId.Length: > 0 })
        {
            OpenSavedLineup(selected.SourceSessionId);
            return;
        }
        EntrantGrid.ItemsSource = selected?.Entrants;
        SelectedHeatTitle.Text = selected is null
            ? "SELECT A RACE"
            : $"{selected.ClassName.ToUpperInvariant()} • {selected.SessionDisplay}";

        RefreshTargetHeats();
        RefreshSelectedMainSeriesEditor(selected);
        if (RosterEditActions is not null) RosterEditActions.IsEnabled = selected is { IsLocked: false } && !_browsingSavedLineups;
    }

    private void RefreshSelectedMainSeriesEditor(HeatPlan? selected)
    {
        if (SelectedMainSeriesText is null ||
            SelectedMainRunsText is null ||
            SelectedMainCountBestText is null)
        {
            return;
        }

        if (CurrentMode != BuildMode.Mains ||
            selected is null ||
            selected.SessionType != SessionType.Race)
        {
            SelectedMainSeriesText.Text = "SELECT A MAIN";
            SelectedMainRunsText.Text = "1";
            SelectedMainCountBestText.Text = "1";
            return;
        }

        var group = HeatPlans
            .Where(h =>
                h.SessionType == SessionType.Race &&
                string.Equals(h.ClassId, selected.ClassId, StringComparison.OrdinalIgnoreCase) &&
                h.MainOrder == selected.MainOrder)
            .OrderBy(h => h.MainLeg)
            .ToList();

        var first = group.FirstOrDefault() ?? selected;
        var mainLabel = MainLetter(Math.Max(0, selected.MainOrder - 1));

        SelectedMainSeriesText.Text =
            $"{selected.ClassName.ToUpperInvariant()} • {mainLabel} MAIN SERIES";

        SelectedMainRunsText.Text =
            Math.Max(1, first.MainRunCount).ToString();

        SelectedMainCountBestText.Text =
            Math.Clamp(first.MainCountBest, 1, Math.Max(1, first.MainRunCount)).ToString();
    }

    private void ApplySelectedMainSeries_Click(object sender, RoutedEventArgs e)
    {
        if (HasLockedPlans) { ShowLockedStructureMessage(); return; }
        if (CurrentMode != BuildMode.Mains ||
            HeatGrid.SelectedItem is not HeatPlan selected ||
            selected.SessionType != SessionType.Race)
        {
            MessageBox.Show(this,
                "Select a main first.",
                "Main runs",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!int.TryParse(SelectedMainRunsText.Text.Trim(), out var runCount) ||
            runCount is < 1 or > 10)
        {
            MessageBox.Show(this,
                "RUNS must be from 1 to 10.",
                "Main runs",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            SelectedMainRunsText.Focus();
            return;
        }

        if (!int.TryParse(SelectedMainCountBestText.Text.Trim(), out var countBest) ||
            countBest < 1 ||
            countBest > runCount)
        {
            MessageBox.Show(this,
                $"COUNT BEST must be from 1 to RUNS ({runCount}).",
                "Main runs",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            SelectedMainCountBestText.Focus();
            return;
        }

        var group = HeatPlans
            .Where(h =>
                h.SessionType == SessionType.Race &&
                string.Equals(h.ClassId, selected.ClassId, StringComparison.OrdinalIgnoreCase) &&
                h.MainOrder == selected.MainOrder)
            .OrderBy(h => h.MainLeg)
            .ToList();

        if (group.Count == 0)
            return;

        var prototype = group[0];

        // Remove extra legs from the bottom when reducing the run count.
        foreach (var extra in group.Where(h => h.MainLeg > runCount).ToList())
            HeatPlans.Remove(extra);

        group = HeatPlans
            .Where(h =>
                h.SessionType == SessionType.Race &&
                string.Equals(h.ClassId, selected.ClassId, StringComparison.OrdinalIgnoreCase) &&
                h.MainOrder == selected.MainOrder)
            .OrderBy(h => h.MainLeg)
            .ToList();

        // Add any missing leg numbers by cloning the current main roster/rule.
        var existingLegs = HeatPlans
            .Where(h =>
                h.SessionType == SessionType.Race &&
                string.Equals(h.ClassId, selected.ClassId, StringComparison.OrdinalIgnoreCase) &&
                h.MainOrder == selected.MainOrder)
            .Select(h => h.MainLeg)
            .ToHashSet();

        for (var leg = 1; leg <= runCount; leg++)
        {
            if (existingLegs.Contains(leg))
                continue;

            var plan = new HeatPlan
            {
                Order = HeatPlans.Count + 1,
                ClassId = prototype.ClassId,
                ClassName = prototype.ClassName,
                SessionType = SessionType.Race,
                Round = prototype.Round,
                StartStyle = prototype.StartStyle,
                QualificationOrder = prototype.QualificationOrder,
                MainSortOrder = prototype.MainSortOrder,
                RaceFormat = prototype.RaceFormat,
                TargetLaps = prototype.TargetLaps,
                MainOrder = prototype.MainOrder,
                MainLeg = leg,
                MainRunCount = runCount,
                MainCountBest = countBest,
                MainCapacity = prototype.MainCapacity,
                BumpInCount = prototype.BumpInCount,
                BumpUpCount = prototype.BumpUpCount
            };

            foreach (var entrant in prototype.Entrants.Select(CloneEntrant))
                plan.Entrants.Add(entrant);

            Renumber(plan.Entrants);
            HeatPlans.Add(plan);
        }

        foreach (var leg in HeatPlans.Where(h =>
                     h.SessionType == SessionType.Race &&
                     string.Equals(h.ClassId, selected.ClassId, StringComparison.OrdinalIgnoreCase) &&
                     h.MainOrder == selected.MainOrder))
        {
            leg.MainRunCount = runCount;
            leg.MainCountBest = countBest;
        }

        RenameClassPlans(selected.ClassId);
        RefreshMainPlanLinks(selected.ClassId);
        AutoSortMainPlans();

        var reselect = HeatPlans.FirstOrDefault(h =>
            h.SessionType == SessionType.Race &&
            string.Equals(h.ClassId, selected.ClassId, StringComparison.OrdinalIgnoreCase) &&
            h.MainOrder == selected.MainOrder &&
            h.MainLeg == Math.Min(selected.MainLeg, runCount));

        HeatGrid.Items.Refresh();
        HeatGrid.SelectedItem = reselect;
        RefreshSelectedMainSeriesEditor(reselect);

        var mainLabel = MainLetter(Math.Max(0, selected.MainOrder - 1));
        BuilderStatusText.Text =
            $"{selected.ClassName} {mainLabel} Main now has {runCount} run(s); best {countBest} count.";
    }

    private void AutoSortMains_Click(object sender, RoutedEventArgs e)
    {
        if (HasLockedPlans) { ShowLockedStructureMessage(); return; }
        if (CurrentMode != BuildMode.Mains)
            return;

        var selected = HeatGrid.SelectedItem as HeatPlan;
        AutoSortMainPlans();
        HeatGrid.Items.Refresh();

        if (selected is not null && HeatPlans.Contains(selected))
            HeatGrid.SelectedItem = selected;

        BuilderStatusText.Text =
            "Mains auto-sorted: lowest mains first, A Mains last.";
    }

    private void AutoSortMainPlans()
    {
        if (HeatPlans.Count <= 1)
        {
            ReorderNumbers();
            return;
        }

        var classOrder = _raceEvent.ClassIds
            .Select((id, index) => new { id, index })
            .ToDictionary(x => x.id, x => x.index, StringComparer.OrdinalIgnoreCase);

        var ordered = HeatPlans
            .OrderByDescending(h => h.SessionType == SessionType.Race ? h.MainOrder : 0)
            .ThenBy(h => classOrder.TryGetValue(h.ClassId, out var index) ? index : int.MaxValue)
            .ThenBy(h => h.SessionType == SessionType.Race ? h.MainLeg : h.Order)
            .ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
        {
            var plan = ordered[targetIndex];
            var currentIndex = HeatPlans.IndexOf(plan);

            if (currentIndex >= 0 && currentIndex != targetIndex)
                HeatPlans.Move(currentIndex, targetIndex);
        }

        ReorderNumbers();
    }

    private void RefreshTargetHeats()
    {
        if (HeatGrid.SelectedItem is not HeatPlan selected)
        {
            TargetHeatCombo.ItemsSource = null;
            return;
        }

        TargetHeatCombo.ItemsSource = HeatPlans
            .Where(h => !h.IsLocked && h != selected && h.ClassId == selected.ClassId &&
                (selected.SessionType != SessionType.Race || h.MainLeg == selected.MainLeg))
            .OrderBy(h => h.Order)
            .ToList();
        TargetHeatCombo.SelectedIndex = TargetHeatCombo.Items.Count > 0 ? 0 : -1;
    }

    private void HeatUp_Click(object sender, RoutedEventArgs e) => MoveHeat(-1);
    private void HeatDown_Click(object sender, RoutedEventArgs e) => MoveHeat(1);

    private void MoveHeat(int offset)
    {
        if (HasLockedPlans) { ShowLockedStructureMessage(); return; }
        if (HeatGrid.SelectedItem is not HeatPlan selected)
            return;

        var index = HeatPlans.IndexOf(selected);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= HeatPlans.Count)
            return;

        HeatPlans.Move(index, target);
        ReorderNumbers();
        HeatGrid.SelectedItem = selected;
        HeatGrid.Items.Refresh();
    }

    private void AddHeat_Click(object sender, RoutedEventArgs e)
    {
        if (HasLockedPlans) { ShowLockedStructureMessage(); return; }
        var selected = HeatGrid.SelectedItem as HeatPlan;
        var cls = selected is null ? SelectedClasses().FirstOrDefault() : _classes.FirstOrDefault(c => c.Id == selected.ClassId);
        if (cls is null)
            return;

        var plan = new HeatPlan
        {
            Order = HeatPlans.Count + 1,
            ClassId = cls.Id,
            ClassName = cls.Name,
            SessionType = CurrentMode == BuildMode.QualifyingRound ? SessionType.Qualifying : SessionType.Race,
            Round = CurrentMode == BuildMode.QualifyingRound ? CurrentRound() : 1,
            StartStyle = CurrentMode == BuildMode.QualifyingRound ? cls.QualStartStyle : cls.RaceStartStyle,
            QualificationOrder = CurrentMode == BuildMode.QualifyingRound ? CurrentQualSort : QualificationOrder.Ranked,
            MainSortOrder = CurrentMode == BuildMode.Mains ? CurrentMainSort : MainSortOrder.QualifyingRank,
            RaceFormat = cls.FormatFor(CurrentMode == BuildMode.QualifyingRound ? SessionType.Qualifying : SessionType.Race),
            TargetLaps = cls.LapsFor(CurrentMode == BuildMode.QualifyingRound ? SessionType.Qualifying : SessionType.Race),
            MainOrder = CurrentMode == BuildMode.Mains
                ? HeatPlans.Where(h => h.ClassId == cls.Id && h.SessionType == SessionType.Race)
                    .Select(h => h.MainOrder)
                    .DefaultIfEmpty(0)
                    .Max() + 1
                : 0,
            MainLeg = 1,
            MainRunCount = 1,
            MainCountBest = 1,
            MainCapacity = CurrentMode == BuildMode.Mains ? Math.Clamp(cls.MaxRacersPerMain, 1, 30) : 0,
            BumpInCount = 0
        };

        HeatPlans.Add(plan);
        RenameClassPlans(cls.Id);
        RefreshMainPlanLinks(cls.Id);

        if (CurrentMode == BuildMode.Mains)
            AutoSortMainPlans();
        else
            ReorderNumbers();

        HeatGrid.SelectedItem = plan;
        HeatGrid.Items.Refresh();
    }

    private void RemoveHeat_Click(object sender, RoutedEventArgs e)
    {
        if (HasLockedPlans) { ShowLockedStructureMessage(); return; }
        if (HeatGrid.SelectedItem is not HeatPlan selected)
            return;

        if (selected.Entrants.Count > 0)
        {
            MessageBox.Show(this, "Move the drivers out of this heat before removing it.",
                "Remove heat", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var classId = selected.ClassId;
        HeatPlans.Remove(selected);
        RenameClassPlans(classId);
        RefreshMainPlanLinks(classId);

        if (CurrentMode == BuildMode.Mains)
            AutoSortMainPlans();
        else
            ReorderNumbers();

        HeatGrid.Items.Refresh();
        HeatGrid.SelectedIndex = HeatPlans.Count > 0 ? 0 : -1;
    }

    private void DriverUp_Click(object sender, RoutedEventArgs e) => MoveDriverOrder(-1);
    private void DriverDown_Click(object sender, RoutedEventArgs e) => MoveDriverOrder(1);

    private void MoveDriverOrder(int offset)
    {
        if (HeatGrid.SelectedItem is not HeatPlan heat ||
            EntrantGrid.SelectedItem is not SessionEntrant entrant)
            return;

        if (heat.IsLocked || _browsingSavedLineups) return;

        var index = heat.Entrants.IndexOf(entrant);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= heat.Entrants.Count)
            return;

        heat.Entrants.Move(index, target);
        Renumber(heat.Entrants);
        EntrantGrid.Items.Refresh();
        EntrantGrid.SelectedItem = entrant;
    }

    private void MoveDriver_Click(object sender, RoutedEventArgs e)
    {
        if (HeatGrid.SelectedItem is not HeatPlan source ||
            EntrantGrid.SelectedItem is not SessionEntrant entrant ||
            TargetHeatCombo.SelectedItem is not HeatPlan target ||
            source.ClassId != target.ClassId)
            return;

        if (_browsingSavedLineups || source.IsLocked || target.IsLocked || !HasRoom(target)) return;
        if (target.Entrants.Any(e => e.DriverId == entrant.DriverId)) return;
        if (!string.IsNullOrWhiteSpace(entrant.TransponderId) && target.Entrants.Any(e =>
            string.Equals(e.TransponderId, entrant.TransponderId, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "That transponder is already assigned in the destination race.",
                "Move driver", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        source.Entrants.Remove(entrant);
        target.Entrants.Add(entrant);
        Renumber(source.Entrants);
        Renumber(target.Entrants);

        HeatGrid.Items.Refresh();
        EntrantGrid.Items.Refresh();
        RefreshTargetHeats();
        BuilderStatusText.Text = $"{entrant.DisplayName} moved to {target.Name}.";
    }

    public Func<Task<bool>>? SaveRequested { get; set; }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_browsingSavedLineups) return;
        if (HeatPlans.Count == 0)
        {
            MessageBox.Show(this, "Generate at least one heat/main before saving.",
                "Rounds / mains", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (HeatPlans.Any(h => h.Entrants.Count > 30))
        {
            MessageBox.Show(this, "This race roster is full. Split the field into another heat/main.",
                "Rounds / mains", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BuiltSessionType = CurrentMode == BuildMode.QualifyingRound ? SessionType.Qualifying : SessionType.Race;
        BuiltRound = CurrentMode == BuildMode.QualifyingRound ? CurrentRound() : 1;
        ReplaceClassIds = SelectedClasses().Select(c => c.Id).ToList();

        var result = new List<EventSessionDefinition>();
        foreach (var plan in HeatPlans)
        {
            if (plan.IsLocked)
            {
                result.Add(_raceEvent.Sessions.Single(s => s.Id == plan.SourceSessionId));
                continue;
            }
            var original = _raceEvent.Sessions.FirstOrDefault(s => s.Id == plan.SourceSessionId);
            var entrants = plan.Entrants.Select(CloneEntrant).ToList();
            Renumber(entrants);

            result.Add(new EventSessionDefinition
            {
                Id = original?.Id ?? Guid.NewGuid().ToString("N"),
                QueueOrder = original?.QueueOrder ?? 0,
                RaceNumber = plan.RaceNumber,
                Name = plan.Name,
                ClassId = plan.ClassId,
                ClassName = plan.ClassName,
                SessionType = plan.SessionType,
                StartStyle = plan.StartStyle,
                RaceFormat = plan.RaceFormat,
                TargetLaps = plan.TargetLaps,
                QualificationOrder = plan.QualificationOrder,
                MainSortOrder = plan.MainSortOrder,
                Round = plan.Round,
                Heat = GetHeatIndex(plan),
                MainCapacity = plan.MainCapacity,
                MainLeg = plan.MainLeg,
                MainRunCount = plan.MainRunCount,
                MainCountBest = plan.MainCountBest,
                BumpInCount = plan.BumpInCount,
                BumpUpCount = plan.BumpUpCount,
                ProgressStatus = original?.ProgressStatus ?? EventSessionStatus.Planned,
                Entrants = entrants
            });
        }

        foreach (var lower in result.Where(s =>
                     s.SessionType == SessionType.Race &&
                     s.ProgressStatus != EventSessionStatus.Completed &&
                     s.BumpUpCount > 0))
        {
            var upper = result
                .Where(s =>
                    s.SessionType == SessionType.Race &&
                    s.ClassId == lower.ClassId &&
                    s.Heat == lower.Heat - 1)
                .OrderBy(s => s.MainLeg)
                .FirstOrDefault();

            lower.BumpTargetSessionId = upper?.Id ?? string.Empty;
        }

        BuiltSessions = result;
        if (SaveRequested is null) { DialogResult = true; return; }
        IsEnabled = false;
        var savedSuccessfully = false;
        try
        {
            if (!await SaveRequested()) return;
            savedSuccessfully = true;
            _deletedSessionIds.Clear();
            _lineupSnapshot = null;
            InitializeSavedFilters();
            ShowSavedLineups();
            BuilderStatusText.Text = "Changes saved. Select another round / class to continue, or CLOSE when finished.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not finish saving: " + ex.Message, "Save lineup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { IsEnabled = true; }
        if (savedSuccessfully && CloseAfterSave) DialogResult = true;
    }

    private int GetHeatIndex(HeatPlan plan)
    {
        if (plan.SessionType == SessionType.Race)
            return Math.Max(1, plan.MainOrder);

        return HeatPlans
            .Where(h => h.ClassId == plan.ClassId && h.Round == plan.Round && h.SessionType == SessionType.Qualifying)
            .OrderBy(h => h.Order)
            .ToList()
            .IndexOf(plan) + 1;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void RenameClassPlans(string classId)
    {
        foreach (var roundGroup in HeatPlans.Where(h => h.ClassId == classId && h.SessionType == SessionType.Qualifying).GroupBy(h => h.Round))
        {
        var qualifying = roundGroup
            .Where(h => h.ClassId == classId && h.SessionType == SessionType.Qualifying)
            .OrderBy(h => h.Order)
            .ToList();

        for (var i = 0; i < qualifying.Count; i++)
        {
            var plan = qualifying[i];

            plan.Name = qualifying.Count == 1
                ? $"{plan.ClassName} Q{plan.Round}"
                : $"{plan.ClassName} Q{plan.Round} Heat {i + 1}";

            plan.SessionDisplay = qualifying.Count == 1
                ? "Qual"
                : $"Qual • Heat {i + 1}/{qualifying.Count}";
        }

        }

        var mains = HeatPlans
            .Where(h => h.ClassId == classId && h.SessionType == SessionType.Race)
            .OrderBy(h => h.MainOrder)
            .ThenBy(h => h.MainLeg)
            .ToList();

        foreach (var plan in mains)
        {
            var mainLabel = MainLetter(Math.Max(0, plan.MainOrder - 1));

            plan.Name = plan.MainRunCount > 1
                ? $"{plan.ClassName} {mainLabel} Main {plan.MainLeg}"
                : $"{plan.ClassName} {mainLabel} Main";

            plan.SessionDisplay = plan.MainRunCount > 1
                ? $"{mainLabel}-Main {plan.MainLeg}/{plan.MainRunCount}"
                : $"{mainLabel}-Main";
        }
    }

    private void ReorderNumbers()
    {
        for (var i = 0; i < HeatPlans.Count; i++)
            HeatPlans[i].Order = i + 1;

        foreach (var classId in HeatPlans.Select(h => h.ClassId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            RenameClassPlans(classId);
            RefreshMainPlanLinks(classId);
        }
        RefreshPreviewRaceNumbers();
    }

    private void RefreshPreviewRaceNumbers()
    {
        // Number a detached preview. Cancelling edits must not alter saved races.
        var existing = _raceEvent.Sessions.Select(session => new EventSessionDefinition {
            ClassId=session.ClassId, ClassName=session.ClassName, SessionType=session.SessionType,
            Round=session.Round, Heat=session.Heat, MainLeg=session.MainLeg,
            QueueOrder=session.QueueOrder, RaceNumber=session.RaceNumber }).ToList();
        var nextQueue = existing.Select(s=>s.QueueOrder).DefaultIfEmpty(0).Max();
        var previews = HeatPlans.Select(plan => new EventSessionDefinition {
            ClassId=plan.ClassId, ClassName=plan.ClassName, SessionType=plan.SessionType,
            Round=plan.Round, Heat=GetHeatIndex(plan), MainLeg=plan.MainLeg,
            QueueOrder=nextQueue+plan.Order,
            RaceNumber=_raceEvent.Sessions.FirstOrDefault(s=>s.Id==plan.SourceSessionId)?.RaceNumber ?? 0 }).ToList();
        RaceSchedule.AssignRaceNumbers(existing.Concat(previews));
        for (var i=0;i<HeatPlans.Count;i++) HeatPlans[i].RaceNumber=previews[i].RaceNumber;
    }

    private static string MainLetter(int index)
    {
        if (index is >= 0 and < 26)
            return ((char)('A' + index)).ToString();
        return (index + 1).ToString();
    }

    private static SessionEntrant CloneEntrant(SessionEntrant source) => new()
    {
        CarNumber = source.CarNumber,
        DriverId = source.DriverId,
        DriverName = source.DriverName,
        Nickname = source.Nickname,
        TransponderId = source.TransponderId,
        SeedRank = source.SeedRank
    };

    private static void Renumber(IList<SessionEntrant> entrants)
    {
        for (var i = 0; i < entrants.Count; i++)
            entrants[i].CarNumber = i + 1;
    }
}
