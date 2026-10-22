using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;

namespace IMRCRaceControl.Dialogs;

public partial class SettingsDialog : Window
{
    public enum ApplicationAction { None, CheckUpdates, ManageData }
    public ApplicationAction RequestedApplicationAction { get; private set; }
    public bool ApplicationToolsEnabled
    {
        set { CheckUpdatesButton.IsEnabled = value; ManageDataButton.IsEnabled = value; }
    }
    private void CheckUpdates_Click(object sender, RoutedEventArgs e) => OpenApplicationTool(ApplicationAction.CheckUpdates,sender,e);
    private void ManageData_Click(object sender, RoutedEventArgs e) => OpenApplicationTool(ApplicationAction.ManageData,sender,e);
    private void OpenApplicationTool(ApplicationAction action,object sender,RoutedEventArgs e)
    {
        RequestedApplicationAction = action;
        Save_Click(sender,e);
        if (DialogResult != true) RequestedApplicationAction = ApplicationAction.None;
    }

    private sealed record Choice<T>(T Value, string Label);

    private readonly ToneService _toneService = new();
    private readonly AppTheme _originalTheme;
    private readonly AppSettings _sourceSettings;
    private bool _saved;
    private bool _dashboardColorsFollowTheme;
    private AppTheme _dashboardPreviewTheme;

    public List<string> SpectatorVisibleColumns => SpectatorColumnChecks.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();
    public bool SpectatorShowHeader => SpectatorHeaderCheck.IsChecked == true;
    public bool SpectatorShowClock => SpectatorClockCheck.IsChecked == true;
    public bool SpectatorShowNextUp => SpectatorNextCheck.IsChecked == true;
    public Action? OpenWeb { get; set; }
    public bool WebShowOverallPosition => WebOverallCheck.IsChecked == true;
    private void OpenWeb_Click(object sender, RoutedEventArgs e) => OpenWeb?.Invoke();
    public string DriverCrossingStyle { get; private set; } = "Box";
    public string DriverCrossingColor { get; private set; } = "#35C76F";
    public string FinishedDriverStyle { get; private set; } = "Row";
    public string FinishedDriverColor { get; private set; } = "#808080";
    private void FinishedColorPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) FinishedDriverColorText.Text = button.Tag?.ToString() ?? "#808080";
    }
    public ObservableCollection<DashboardColumnOption> DashboardColumnOptions { get; } = new();
    public ObservableCollection<PracticeStatOption> PracticeStatOptions { get; } = new();

    public AppTheme SelectedTheme { get; private set; }
    public TonePattern StartTone { get; private set; }
    public ToneLength StartLength { get; private set; }
    public TonePattern MinimumTone { get; private set; }
    public ToneLength MinimumLength { get; private set; }
    public TonePattern EndTone { get; private set; }
    public ToneLength EndLength { get; private set; }
    public TonePattern PassingTone { get; private set; }
    public ToneLength PassingLength { get; private set; }

    public string StartToneFile { get; private set; } = string.Empty;
    public string MinimumToneFile { get; private set; } = string.Empty;
    public string EndToneFile { get; private set; } = string.Empty;
    public string PassingToneFile { get; private set; } = string.Empty;

    public IReadOnlyList<string> VisibleDashboardColumns =>
        DashboardColumnOptions.Where(c => c.IsVisible).Select(c => c.Id).ToList();

    public IReadOnlyList<string> VisiblePracticeStats =>
        PracticeStatOptions.Where(c => c.IsVisible).Select(c => c.Id).ToList();

    public int PracticeCardsPerRow { get; private set; } = 5;

    public string DashboardBackgroundColor { get; private set; } = string.Empty;
    public string DashboardPanelColor { get; private set; } = string.Empty;
    public string DashboardRowColor { get; private set; } = string.Empty;
    public bool DashboardAlternateRowsEnabled { get; private set; } = true;
    public string DashboardAlternateRowColor { get; private set; } = string.Empty;
    public string DashboardTextColor { get; private set; } = string.Empty;
    public string DashboardHighlightColor { get; private set; } = string.Empty;
    public string DashboardFastestLapHighlightColor { get; private set; } = string.Empty;
    public string DashboardNewBestLapHighlightColor { get; private set; } = string.Empty;
    public string DashboardCheckInGlowColor { get; private set; } = string.Empty;
    public string DashboardMinimumLapGlowColor { get; private set; } = string.Empty;
    public double DashboardMinimumLapGlowSeconds { get; private set; } = 1.0;
    public string DashboardPositionUpColor { get; private set; } = string.Empty;
    public string DashboardPositionDownColor { get; private set; } = string.Empty;
    public string DashboardPositionSameColor { get; private set; } = string.Empty;
    public bool DashboardColorsFollowTheme { get; private set; } = true;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        InitializeLapSpeech(settings);
        InitializeRaceAudio(settings);
        DataContext = this;

        _sourceSettings = settings;
        WebOverallCheck.IsChecked = settings.WebShowOverallPosition;
        SpectatorHeaderCheck.IsChecked = settings.SpectatorShowHeader;
        SpectatorClockCheck.IsChecked = settings.SpectatorShowClock;
        SpectatorNextCheck.IsChecked = settings.SpectatorShowNextUp;
        var spectatorColumns = settings.SpectatorVisibleColumns ?? settings.DashboardVisibleColumns;
        foreach (var id in DashboardColumnCatalog.OptionalColumns.Where(c => c != "MANUAL"))
            SpectatorColumnChecks.Children.Add(new CheckBox { Content = id, Tag = id, Width = 170, Margin = new Thickness(0,4,0,4), IsChecked = spectatorColumns.Contains(id) });
        _originalTheme = settings.Theme;
        SelectedTheme = settings.Theme;
        StartTone = settings.StartTone;
        StartLength = settings.StartToneLength;
        MinimumTone = settings.MinimumLapTone;
        MinimumLength = settings.MinimumLapToneLength;
        EndTone = settings.EndTone;
        EndLength = settings.EndToneLength;
        PassingTone = settings.PassingTone;
        PassingLength = settings.PassingToneLength;

        StartToneFile = settings.StartToneFile;
        MinimumToneFile = settings.MinimumLapToneFile;
        EndToneFile = settings.EndToneFile;
        PassingToneFile = settings.PassingToneFile;

        StartFileText.Text = StartToneFile;
        MinimumFileText.Text = MinimumToneFile;
        EndFileText.Text = EndToneFile;
        PassingFileText.Text = PassingToneFile;

        _dashboardColorsFollowTheme = settings.DashboardColorsFollowTheme;
        _dashboardPreviewTheme = settings.Theme;
        DashboardAlternateRowsCheck.IsChecked = settings.DashboardAlternateRowsEnabled;

        if (_dashboardColorsFollowTheme)
        {
            SetDashboardColorFields(ThemeService.GetDashboardDefaults(settings.Theme));
        }
        else
        {
            DashboardBackgroundText.Text = settings.DashboardBackgroundColor;
            DashboardPanelText.Text = settings.DashboardPanelColor;
            DashboardRowText.Text = settings.DashboardRowColor;
            DashboardAlternateRowText.Text = settings.DashboardAlternateRowColor;
            DashboardTextText.Text = settings.DashboardTextColor;
            DashboardHighlightText.Text = settings.DashboardPassingHighlightColor;
            DashboardFastestLapText.Text = settings.DashboardFastestLapHighlightColor;
            DashboardNewBestLapText.Text = settings.DashboardNewBestLapHighlightColor;
            DashboardCheckInGlowText.Text = settings.DashboardCheckInGlowColor;
            DashboardMinimumLapGlowText.Text = settings.DashboardMinimumLapGlowColor;
            DashboardPositionUpText.Text = settings.DashboardPositionUpColor;
            DashboardPositionDownText.Text = settings.DashboardPositionDownColor;
            DashboardPositionSameText.Text = settings.DashboardPositionSameColor;
        }

        CrossingStyleCombo.SelectedIndex = settings.DriverCrossingStyle == "Underline" ? 1 : 0;
        DriverCrossingColorText.Text = settings.DriverCrossingColor;
        FinishedStyleCombo.SelectedIndex = settings.FinishedDriverStyle == "Text" ? 1 : settings.FinishedDriverStyle == "None" ? 2 : 0;
        FinishedDriverColorText.Text = settings.FinishedDriverColor;
        DashboardMinimumLapGlowSecondsText.Text =
            settings.DashboardMinimumLapGlowSeconds.ToString("0.###", CultureInfo.InvariantCulture);

        var visible = settings.DashboardVisibleColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in DashboardColumnCatalog.OptionalColumns)
        {
            DashboardColumnOptions.Add(new DashboardColumnOption
            {
                Id = id,
                Label = id,
                IsVisible = visible.Contains(id)
            });
        }

        var visiblePracticeStats = settings.PracticeVisibleStats
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var id in PracticeStatCatalog.All)
        {
            PracticeStatOptions.Add(new PracticeStatOption
            {
                Id = id,
                Label = id,
                IsVisible = visiblePracticeStats.Contains(id)
            });
        }

        var practiceCardChoices = new[]
        {
            new Choice<int>(4, "4 cards per row • 8 full-size cards"),
            new Choice<int>(5, "5 cards per row • 10 full-size cards"),
            new Choice<int>(6, "6 cards per row • 12 full-size cards")
        }.ToList();
        SetupCombo(
            PracticeCardsPerRowCombo,
            practiceCardChoices,
            settings.PracticeCardsPerRow is >= 4 and <= 6 ? settings.PracticeCardsPerRow : 5);

        var themes = Enum.GetValues<AppTheme>()
            .Select(v => new Choice<AppTheme>(v, v.ToDisplay()))
            .ToList();
        var tones = Enum.GetValues<TonePattern>()
            .Select(v => new Choice<TonePattern>(v, v.ToDisplay()))
            .ToList();
        var lengths = Enum.GetValues<ToneLength>()
            .Select(v => new Choice<ToneLength>(v, v.ToDisplay()))
            .ToList();

        SetupCombo(ThemeCombo, themes, settings.Theme);
        SetupCombo(StartToneCombo, tones, settings.StartTone);
        SetupCombo(MinimumToneCombo, tones, settings.MinimumLapTone);
        SetupCombo(EndToneCombo, tones, settings.EndTone);
        SetupCombo(PassingToneCombo, tones, settings.PassingTone);
        SetupCombo(StartLengthCombo, lengths, settings.StartToneLength);
        SetupCombo(MinimumLengthCombo, lengths, settings.MinimumLapToneLength);
        SetupCombo(EndLengthCombo, lengths, settings.EndToneLength);
        SetupCombo(PassingLengthCombo, lengths, settings.PassingToneLength);
        InitializeExtraTones(settings, tones, lengths);

        Closed += (_, _) =>
        {
            if (!_saved)
            {
                ThemeService.Apply(_originalTheme);
                ThemeService.ApplyDashboard(_sourceSettings);
            }
        };
    }

    private static void SetupCombo<T>(ComboBox combo, IReadOnlyList<Choice<T>> items, T selected)
    {
        combo.ItemsSource = items;
        combo.DisplayMemberPath = nameof(Choice<T>.Label);
        combo.SelectedValuePath = nameof(Choice<T>.Value);
        combo.SelectedValue = selected;
    }

    private static T Selected<T>(ComboBox combo, T fallback) =>
        combo.SelectedValue is T value ? value : fallback;

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var theme = Selected(ThemeCombo, _originalTheme);

        // If the operator is still using theme defaults, changing the application
        // theme also changes the dashboard palette immediately. Custom dashboard
        // colors remain custom until RESET TO THEME DEFAULTS is pressed.
        if (_dashboardColorsFollowTheme || CurrentFieldsMatchThemeDefaults(_dashboardPreviewTheme))
        {
            _dashboardColorsFollowTheme = true;
            SetDashboardColorFields(ThemeService.GetDashboardDefaults(theme));
        }

        _dashboardPreviewTheme = theme;
        ThemeService.Apply(theme);
        ApplyDashboardPreview(theme);
    }

    private void BrowseStart_Click(object sender, RoutedEventArgs e) => BrowseAudio(StartFileText);
    private void BrowseMinimum_Click(object sender, RoutedEventArgs e) => BrowseAudio(MinimumFileText);
    private void BrowseEnd_Click(object sender, RoutedEventArgs e) => BrowseAudio(EndFileText);
    private void BrowsePassing_Click(object sender, RoutedEventArgs e) => BrowseAudio(PassingFileText);

    private void ClearStart_Click(object sender, RoutedEventArgs e) => StartFileText.Clear();
    private void ClearMinimum_Click(object sender, RoutedEventArgs e) => MinimumFileText.Clear();
    private void ClearEnd_Click(object sender, RoutedEventArgs e) => EndFileText.Clear();
    private void ClearPassing_Click(object sender, RoutedEventArgs e) => PassingFileText.Clear();

    private static void BrowseAudio(TextBox target)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose race audio",
            Filter = "Supported audio (*.wav;*.mp3;*.wma)|*.wav;*.mp3;*.wma|WAV (*.wav)|*.wav|MP3 (*.mp3)|*.mp3|WMA (*.wma)|*.wma",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
            target.Text = dialog.FileName;
    }

    private void TestStart_Click(object sender, RoutedEventArgs e) =>
        _toneService.Play(Selected(StartToneCombo, StartTone), Selected(StartLengthCombo, StartLength), StartFileText.Text.Trim());

    private void TestMinimum_Click(object sender, RoutedEventArgs e) =>
        _toneService.Play(Selected(MinimumToneCombo, MinimumTone), Selected(MinimumLengthCombo, MinimumLength), MinimumFileText.Text.Trim());

    private void TestEnd_Click(object sender, RoutedEventArgs e) =>
        _toneService.Play(Selected(EndToneCombo, EndTone), Selected(EndLengthCombo, EndLength), EndFileText.Text.Trim());

    private void TestPassing_Click(object sender, RoutedEventArgs e) =>
        _toneService.Play(Selected(PassingToneCombo, PassingTone), Selected(PassingLengthCombo, PassingLength), PassingFileText.Text.Trim());

    private void ResetDashboardColors_Click(object sender, RoutedEventArgs e)
    {
        var theme = Selected(ThemeCombo, _originalTheme);
        _dashboardColorsFollowTheme = true;
        _dashboardPreviewTheme = theme;
        DashboardAlternateRowsCheck.IsChecked = true;
        SetDashboardColorFields(ThemeService.GetDashboardDefaults(theme));
        ThemeService.Apply(theme);
        ApplyDashboardPreview(theme);
    }

    private void DashboardColorPreview_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        _dashboardColorsFollowTheme = false;
        ApplyDashboardPreview(Selected(ThemeCombo, _dashboardPreviewTheme));
    }

    private void DashboardColorPreview_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        _dashboardColorsFollowTheme = false;
        ApplyDashboardPreview(Selected(ThemeCombo, _dashboardPreviewTheme));
    }

    private void SetDashboardColorFields(DashboardThemeColors colors)
    {
        DashboardBackgroundText.Text = colors.Background;
        DashboardPanelText.Text = colors.Panel;
        DashboardRowText.Text = colors.Row;
        DashboardAlternateRowText.Text = colors.AlternateRow;
        DashboardTextText.Text = colors.Text;
        DashboardHighlightText.Text = colors.PassingHighlight;
        DashboardFastestLapText.Text = colors.FastestLapHighlight;
        DashboardNewBestLapText.Text = colors.NewBestLapHighlight;
        DashboardCheckInGlowText.Text = colors.CheckInGlow;
        DashboardMinimumLapGlowText.Text = colors.MinimumLapGlow;
        DashboardPositionUpText.Text = colors.PositionUp;
        DashboardPositionDownText.Text = colors.PositionDown;
        DashboardPositionSameText.Text = colors.PositionSame;
    }

    private DashboardThemeColors ReadDashboardColorFields(AppTheme theme)
    {
        var defaults = ThemeService.GetDashboardDefaults(theme);
        return new DashboardThemeColors(
            ThemeService.IsValidColor(DashboardBackgroundText.Text) ? DashboardBackgroundText.Text.Trim() : defaults.Background,
            ThemeService.IsValidColor(DashboardPanelText.Text) ? DashboardPanelText.Text.Trim() : defaults.Panel,
            ThemeService.IsValidColor(DashboardRowText.Text) ? DashboardRowText.Text.Trim() : defaults.Row,
            ThemeService.IsValidColor(DashboardAlternateRowText.Text) ? DashboardAlternateRowText.Text.Trim() : defaults.AlternateRow,
            ThemeService.IsValidColor(DashboardTextText.Text) ? DashboardTextText.Text.Trim() : defaults.Text,
            ThemeService.IsValidColor(DashboardHighlightText.Text) ? DashboardHighlightText.Text.Trim() : defaults.PassingHighlight,
            ThemeService.IsValidColor(DashboardFastestLapText.Text) ? DashboardFastestLapText.Text.Trim() : defaults.FastestLapHighlight,
            ThemeService.IsValidColor(DashboardNewBestLapText.Text) ? DashboardNewBestLapText.Text.Trim() : defaults.NewBestLapHighlight,
            ThemeService.IsValidColor(DashboardCheckInGlowText.Text) ? DashboardCheckInGlowText.Text.Trim() : defaults.CheckInGlow,
            ThemeService.IsValidColor(DashboardMinimumLapGlowText.Text) ? DashboardMinimumLapGlowText.Text.Trim() : defaults.MinimumLapGlow,
            ThemeService.IsValidColor(DashboardPositionUpText.Text) ? DashboardPositionUpText.Text.Trim() : defaults.PositionUp,
            ThemeService.IsValidColor(DashboardPositionDownText.Text) ? DashboardPositionDownText.Text.Trim() : defaults.PositionDown,
            ThemeService.IsValidColor(DashboardPositionSameText.Text) ? DashboardPositionSameText.Text.Trim() : defaults.PositionSame);
    }

    private void ApplyDashboardPreview(AppTheme theme)
    {
        var colors = ReadDashboardColorFields(theme);
        if (DashboardAlternateRowsCheck.IsChecked != true)
            colors = colors with { AlternateRow = colors.Row };

        ThemeService.ApplyDashboardColors(colors);
    }

    private bool CurrentFieldsMatchThemeDefaults(AppTheme theme) =>
        ThemeService.DashboardColorsMatchDefaults(
            theme,
            DashboardBackgroundText.Text,
            DashboardPanelText.Text,
            DashboardRowText.Text,
            DashboardAlternateRowText.Text,
            DashboardTextText.Text,
            DashboardHighlightText.Text,
            DashboardFastestLapText.Text,
            DashboardNewBestLapText.Text,
            DashboardCheckInGlowText.Text,
            DashboardMinimumLapGlowText.Text,
            DashboardPositionUpText.Text,
            DashboardPositionDownText.Text,
            DashboardPositionSameText.Text);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!ThemeService.IsValidColor(DriverCrossingColorText.Text.Trim()) || !ThemeService.IsValidColor(FinishedDriverColorText.Text.Trim()))
        {
            MessageBox.Show(this, "Enter valid crossing and finished-driver colors (for example #35C76F or #808080).", "Dashboard colors");
            return;
        }
        DriverCrossingStyle = CrossingStyleCombo.SelectedIndex == 1 ? "Underline" : "Box";
        DriverCrossingColor = DriverCrossingColorText.Text.Trim();
        FinishedDriverStyle = FinishedStyleCombo.SelectedIndex == 1 ? "Text" : FinishedStyleCombo.SelectedIndex == 2 ? "None" : "Row";
        FinishedDriverColor = FinishedDriverColorText.Text.Trim();
        var customPaths = new[]
        {
            StartFileText.Text.Trim(),
            MinimumFileText.Text.Trim(),
            EndFileText.Text.Trim(),
            PassingFileText.Text.Trim(),
            FirstCrossingFileText.Text.Trim(),
            DriverFinishFileText.Text.Trim(),
            HalfwayFileText.Text.Trim()
        };

        foreach (var path in customPaths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            if (!File.Exists(path))
            {
                MessageBox.Show(this, $"Audio file not found:\n{path}", "Audio cues",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!ToneService.IsSupportedFile(path))
            {
                MessageBox.Show(this, "Custom audio must be WAV, MP3, or WMA.", "Audio cues",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        var dashboardColors = new[]
        {
            DashboardBackgroundText.Text.Trim(),
            DashboardPanelText.Text.Trim(),
            DashboardRowText.Text.Trim(),
            DashboardAlternateRowText.Text.Trim(),
            DashboardTextText.Text.Trim(),
            DashboardHighlightText.Text.Trim(),
            DashboardFastestLapText.Text.Trim(),
            DashboardNewBestLapText.Text.Trim(),
            DashboardCheckInGlowText.Text.Trim(),
            DashboardMinimumLapGlowText.Text.Trim(),
            DashboardPositionUpText.Text.Trim(),
            DashboardPositionDownText.Text.Trim(),
            DashboardPositionSameText.Text.Trim()
        };
        if (dashboardColors.Any(c => !ThemeService.IsValidColor(c)))
        {
            MessageBox.Show(this,
                "Dashboard colors must be valid colors such as #0B2030 or #FFD54F. Use RESET TO THEME DEFAULTS to restore the selected theme's palette.",
                "Dashboard colors", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!double.TryParse(
                DashboardMinimumLapGlowSecondsText.Text.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var minimumLapGlowSeconds) ||
            minimumLapGlowSeconds < 0.1 ||
            minimumLapGlowSeconds > 30.0)
        {
            MessageBox.Show(this,
                "Below-minimum glow duration must be between 0.1 and 30 seconds.",
                "Dashboard glow duration",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            SelectedTheme = Selected(ThemeCombo, SelectedTheme);
            StartTone = Selected(StartToneCombo, StartTone);
            MinimumTone = Selected(MinimumToneCombo, MinimumTone);
            EndTone = Selected(EndToneCombo, EndTone);
            PassingTone = Selected(PassingToneCombo, PassingTone);
            StartLength = Selected(StartLengthCombo, StartLength);
            MinimumLength = Selected(MinimumLengthCombo, MinimumLength);
            EndLength = Selected(EndLengthCombo, EndLength);
            PassingLength = Selected(PassingLengthCombo, PassingLength);
            PracticeCardsPerRow = Selected(PracticeCardsPerRowCombo, PracticeCardsPerRow);
            PracticeCardsPerRow = Math.Clamp(PracticeCardsPerRow, 4, 6);

            StartToneFile = ImportIfSelected(customPaths[0], "start");
            MinimumToneFile = ImportIfSelected(customPaths[1], "minimum_lap");
            EndToneFile = ImportIfSelected(customPaths[2], "end");
            PassingToneFile = ImportIfSelected(customPaths[3], "passing");
            SaveExtraTones(customPaths);

            DashboardBackgroundColor = dashboardColors[0];
            DashboardPanelColor = dashboardColors[1];
            DashboardRowColor = dashboardColors[2];
            DashboardAlternateRowsEnabled = DashboardAlternateRowsCheck.IsChecked == true;
            DashboardAlternateRowColor = dashboardColors[3];
            DashboardTextColor = dashboardColors[4];
            DashboardHighlightColor = dashboardColors[5];
            DashboardFastestLapHighlightColor = dashboardColors[6];
            DashboardNewBestLapHighlightColor = dashboardColors[7];
            DashboardCheckInGlowColor = dashboardColors[8];
            DashboardMinimumLapGlowColor = dashboardColors[9];
            DashboardPositionUpColor = dashboardColors[10];
            DashboardPositionDownColor = dashboardColors[11];
            DashboardPositionSameColor = dashboardColors[12];
            DashboardMinimumLapGlowSeconds = minimumLapGlowSeconds;
            DashboardColorsFollowTheme = ThemeService.DashboardColorsMatchDefaults(
                SelectedTheme,
                DashboardBackgroundColor,
                DashboardPanelColor,
                DashboardRowColor,
                DashboardAlternateRowColor,
                DashboardTextColor,
                DashboardHighlightColor,
                DashboardFastestLapHighlightColor,
                DashboardNewBestLapHighlightColor,
                DashboardCheckInGlowColor,
                DashboardMinimumLapGlowColor,
                DashboardPositionUpColor,
                DashboardPositionDownColor,
                DashboardPositionSameColor);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _saved = true;
        ThemeService.Apply(SelectedTheme);

        var savedPreview = new DashboardThemeColors(
            DashboardBackgroundColor,
            DashboardPanelColor,
            DashboardRowColor,
            DashboardAlternateRowsEnabled ? DashboardAlternateRowColor : DashboardRowColor,
            DashboardTextColor,
            DashboardHighlightColor,
            DashboardFastestLapHighlightColor,
            DashboardNewBestLapHighlightColor,
            DashboardCheckInGlowColor,
            DashboardMinimumLapGlowColor,
            DashboardPositionUpColor,
            DashboardPositionDownColor,
            DashboardPositionSameColor);
        ThemeService.ApplyDashboardColors(savedPreview);

        DialogResult = true;
    }

    private static string EffectiveToneKey(string customPath, TonePattern pattern) =>
        string.IsNullOrWhiteSpace(customPath)
            ? $"builtin:{pattern}"
            : $"file:{Path.GetFullPath(customPath)}";

    private static string ImportIfSelected(string path, string cueName) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : ToneService.ImportCustomTone(path, cueName);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ThemeService.Apply(_originalTheme);
        ThemeService.ApplyDashboard(_sourceSettings);
        DialogResult = false;
    }
}
