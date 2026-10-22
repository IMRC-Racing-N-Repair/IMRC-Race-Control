using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Dialogs;

public partial class ClassEditorDialog : Window
{
    public string ClassName => ClassNameText.Text.Trim();
    public RaceFormat RaceFormat { get; private set; } = global::IMRCRaceControl.Models.RaceFormat.Timed;
    public RaceFormat MainRaceFormat { get; private set; }
    public double MainRaceMinutes { get; private set; }
    public int MainTargetLaps { get; private set; }
    public double RaceMinutes { get; private set; }
    public int TargetLaps { get; private set; }
    public double MinimumLapSeconds { get; private set; }
    public int RacersPerHeat { get; private set; }
    public int RacersPerMain { get; private set; }
    public ObservableCollection<MainRuleDefinition> MainRules { get; } = [];

    public StartStyle QualStartStyle => StartStyleExtensions.FromDisplay(SelectedText(QualStyleCombo));
    public StartStyle RaceStartStyle => StartStyleExtensions.FromDisplay(SelectedText(RaceStyleCombo));

    public ClassEditorDialog(ClassDefinition? cls = null)
    {
        InitializeComponent();
        DataContext = this;

        if (cls is null)
        {
            DialogTitleText.Text = "CREATE CLASS";
            SelectComboByText(RaceFormatCombo, global::IMRCRaceControl.Models.RaceFormat.Timed.ToDisplay());
            SelectComboByText(QualStyleCombo, StartStyle.IndividualCrossing.ToDisplay());
            SelectComboByText(RaceStyleCombo, StartStyle.Tone.ToDisplay());

            MainRules.Add(new MainRuleDefinition
            {
                Order = 1,
                MaxRacers = 19,
                BumpInsFromLower = 0,
                RunCount = 1,
                CountBest = 1
            });
        }
        else
        {
            DialogTitleText.Text = $"EDIT CLASS • {cls.Name}";
            ClassNameText.Text = cls.Name;
            RaceMinutesText.Text = cls.RaceMinutes.ToString("0.###");
            TargetLapsText.Text = cls.TargetLaps.ToString();
            MinimumLapText.Text = cls.MinimumLapSeconds.ToString("0.###");
            RacersPerHeatText.Text = cls.MaxRacersPerHeat.ToString();
            RacersPerMainText.Text = cls.MaxRacersPerMain.ToString();

            SelectComboByText(RaceFormatCombo, cls.RaceFormat.ToDisplay());
            SelectComboByText(QualStyleCombo, cls.QualStartStyle.ToDisplay());
            SelectComboByText(RaceStyleCombo, cls.RaceStartStyle.ToDisplay());

            foreach (var rule in (cls.MainRules ?? [])
                         .OrderBy(r => r.Order)
                         .Take(26))
            {
                MainRules.Add(rule.Clone());
            }

            if (MainRules.Count == 0)
            {
                MainRules.Add(new MainRuleDefinition
                {
                    Order = 1,
                    MaxRacers = cls.MaxRacersPerMain,
                    BumpInsFromLower = 0,
                    RunCount = 1,
                    CountBest = 1
                });
            }
        }

        SelectComboByText(MainRaceFormatCombo, (cls?.FormatFor(SessionType.Race) ?? global::IMRCRaceControl.Models.RaceFormat.Timed).ToDisplay());
        MainMinutesText.Text = (cls?.MinutesFor(SessionType.Race) ?? 5).ToString("0.###");
        MainLapsText.Text = (cls?.LapsFor(SessionType.Race) ?? 50).ToString();
        RenumberMainRules();
        UpdateRaceFormatVisibility();

        Loaded += (_, _) => ClassNameText.Focus();
    }

    private void RaceFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateRaceFormatVisibility();

    private void UpdateRaceFormatVisibility()
    {
        if (TimedLengthPanel is null || LapLengthPanel is null || MainMinutesPanel is null || MainLapsPanel is null) return;
        var qualLaps = SelectedText(RaceFormatCombo) == global::IMRCRaceControl.Models.RaceFormat.Laps.ToDisplay();
        var mainLaps = SelectedText(MainRaceFormatCombo) == global::IMRCRaceControl.Models.RaceFormat.Laps.ToDisplay();
        TimedLengthPanel.Visibility = qualLaps ? Visibility.Collapsed : Visibility.Visible;
        LapLengthPanel.Visibility = qualLaps ? Visibility.Visible : Visibility.Collapsed;
        MainMinutesPanel.Visibility = mainLaps ? Visibility.Collapsed : Visibility.Visible;
        MainLapsPanel.Visibility = mainLaps ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddMainRule_Click(object sender, RoutedEventArgs e)
    {
        if (MainRules.Count >= 26)
        {
            MessageBox.Show(this, "A maximum of 26 mains (A–Z) can be configured.",
                "Main rules", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var size = int.TryParse(RacersPerMainText.Text, out var parsed)
            ? Math.Clamp(parsed, 1, 30)
            : 19;

        MainRules.Add(new MainRuleDefinition
        {
            Order = MainRules.Count + 1,
            MaxRacers = size,
            BumpInsFromLower = 0,
            RunCount = 1,
            CountBest = 1
        });

        RenumberMainRules();
        MainRulesGrid.Items.Refresh();
        MainRulesGrid.SelectedIndex = MainRules.Count - 1;
    }

    private void RemoveMainRule_Click(object sender, RoutedEventArgs e)
    {
        if (MainRules.Count <= 1)
        {
            MessageBox.Show(this, "At least A Main must remain in the class.",
                "Main rules", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MainRules.RemoveAt(MainRules.Count - 1);
        RenumberMainRules();
        MainRulesGrid.Items.Refresh();
    }

    private void ApplyMainSize_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RacersPerMainText.Text, out var size) || size is < 1 or > 30)
        {
            MessageBox.Show(this, "Default racers per main must be from 1 to 30.",
                "Main rules", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var rule in MainRules)
            rule.MaxRacers = size;

        MainRulesGrid.Items.Refresh();
    }

    private void RenumberMainRules()
    {
        for (var i = 0; i < MainRules.Count; i++)
        {
            MainRules[i].Order = i + 1;
            MainRules[i].RunCount = Math.Clamp(MainRules[i].RunCount, 1, 10);
            MainRules[i].CountBest = Math.Clamp(
                MainRules[i].CountBest,
                1,
                MainRules[i].RunCount);
        }

        if (MainRules.Count > 0)
            MainRules[^1].BumpInsFromLower = 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ClassName))
        {
            MessageBox.Show(this, "Enter a class name.", "Class", MessageBoxButton.OK, MessageBoxImage.Information);
            ClassNameText.Focus();
            return;
        }

        RaceFormat = string.Equals(
            SelectedText(RaceFormatCombo),
            global::IMRCRaceControl.Models.RaceFormat.Laps.ToDisplay(),
            StringComparison.OrdinalIgnoreCase)
            ? global::IMRCRaceControl.Models.RaceFormat.Laps
            : global::IMRCRaceControl.Models.RaceFormat.Timed;

        MainRaceFormat = SelectedText(MainRaceFormatCombo) == global::IMRCRaceControl.Models.RaceFormat.Laps.ToDisplay()
            ? global::IMRCRaceControl.Models.RaceFormat.Laps : global::IMRCRaceControl.Models.RaceFormat.Timed;

        if (!double.TryParse(RaceMinutesText.Text, out var minutes) || minutes <= 0)
        {
            if (RaceFormat == global::IMRCRaceControl.Models.RaceFormat.Timed)
            {
                MessageBox.Show(this, "Timed race length must be greater than zero minutes.",
                    "Class", MessageBoxButton.OK, MessageBoxImage.Information);
                RaceMinutesText.Focus();
                return;
            }

            minutes = 5;
        }

        if (!int.TryParse(TargetLapsText.Text, out var targetLaps) || targetLaps is < 1 or > 10000)
        {
            if (RaceFormat == global::IMRCRaceControl.Models.RaceFormat.Laps)
            {
                MessageBox.Show(this, "Target laps must be from 1 to 10,000.",
                    "Class", MessageBoxButton.OK, MessageBoxImage.Information);
                TargetLapsText.Focus();
                return;
            }

            targetLaps = 50;
        }

        if (!double.TryParse(MinimumLapText.Text, out var minLap) || minLap < 0)
        {
            MessageBox.Show(this, "Minimum lap must be zero or greater.",
                "Class", MessageBoxButton.OK, MessageBoxImage.Information);
            MinimumLapText.Focus();
            return;
        }

        if (!int.TryParse(RacersPerHeatText.Text, out var heat) || heat is < 1 or > 30)
        {
            MessageBox.Show(this, "Racers per qualifying heat must be from 1 to 30.",
                "Class", MessageBoxButton.OK, MessageBoxImage.Information);
            RacersPerHeatText.Focus();
            return;
        }

        if (!int.TryParse(RacersPerMainText.Text, out var main) || main is < 1 or > 30)
        {
            MessageBox.Show(this, "Default racers per main must be from 1 to 30.",
                "Class", MessageBoxButton.OK, MessageBoxImage.Information);
            RacersPerMainText.Focus();
            return;
        }

        if (MainRules.Count == 0)
        {
            MessageBox.Show(this, "At least A Main must be configured.",
                "Class", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RenumberMainRules();

        foreach (var rule in MainRules)
        {
            if (rule.MaxRacers is < 1 or > 30)
            {
                MessageBox.Show(this, $"{rule.MainName} maximum racers must be from 1 to 30.",
                    "Main rules", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (rule.BumpInsFromLower < 0 || rule.BumpInsFromLower >= rule.MaxRacers)
            {
                MessageBox.Show(this,
                    $"{rule.MainName} bump-ins must be from 0 to {Math.Max(0, rule.MaxRacers - 1)}.",
                    "Main rules", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (rule.RunCount is < 1 or > 10)
            {
                MessageBox.Show(this, $"{rule.MainName} runs must be from 1 to 10.",
                    "Main rules", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (rule.CountBest < 1 || rule.CountBest > rule.RunCount)
            {
                MessageBox.Show(this,
                    $"{rule.MainName} COUNT BEST must be from 1 to its RUNS value ({rule.RunCount}).",
                    "Main rules", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        // No lower main exists beneath the final configured rule.
        MainRules[^1].BumpInsFromLower = 0;

        var mainTimed = MainRaceFormat == global::IMRCRaceControl.Models.RaceFormat.Timed;
        var validMinutes = double.TryParse(MainMinutesText.Text, out var mainMinutes) && double.IsFinite(mainMinutes) && mainMinutes > 0 && mainMinutes <= 1440;
        var validLaps = int.TryParse(MainLapsText.Text, out var mainLaps) && mainLaps >= 1 && mainLaps <= 10000;
        if ((mainTimed && !validMinutes) || (!mainTimed && !validLaps))
        { MessageBox.Show(this, "Enter valid mains minutes (up to 1440) or target laps (1–10000)."); return; }
        MainRaceMinutes = validMinutes ? mainMinutes : 5;
        MainTargetLaps = validLaps ? mainLaps : 50;
        RaceMinutes = minutes;
        TargetLaps = targetLaps;
        MinimumLapSeconds = minLap;
        RacersPerHeat = heat;
        RacersPerMain = main;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static string SelectedText(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item)
            return item.Content?.ToString() ?? string.Empty;
        return combo.SelectedItem?.ToString() ?? combo.Text;
    }

    private static void SelectComboByText(ComboBox combo, string text)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem cbi &&
                string.Equals(cbi.Content?.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = cbi;
                return;
            }
        }
    }
}
