using System.Windows;
using System.Windows.Controls;
using IMRCRaceControl.Models;
namespace IMRCRaceControl.Dialogs;
public partial class RoundMainBuilderDialog
{
    private sealed record GeneratorSort(object Value, string Label);
    private void OpenCompactGenerator()
    {
        var window = new Window { Owner = this, Title = "Generate Round / Mains", Width = 780,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "NEW ROUND / MAINS", FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,16) });
        var grid = new Grid();
        foreach (var width in new[] { 170d, 220d, 75d, 245d }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        var mode = new ComboBox { ItemsSource = ModeCombo.ItemsSource, DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedValue = BuildMode.QualifyingRound };
        var cls = new ComboBox { ItemsSource = ClassCombo.ItemsSource, DisplayMemberPath = "Label", SelectedIndex = 0 };
        var round = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4,8,0,0) };
        var sort = new ComboBox { DisplayMemberPath = "Label", SelectedValuePath = "Value" };
        void Field(int column, string label, UIElement control)
        {
            var field = new StackPanel { Margin = new Thickness(0,0,10,0) };
            field.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0,0,0,4) }); field.Children.Add(control);
            Grid.SetColumn(field,column); grid.Children.Add(field);
        }
        int NextRound()
        {
            var choice = cls.SelectedItem as ClassChoice;
            return _raceEvent.Sessions.Where(s => s.SessionType == SessionType.Qualifying &&
                (choice is null || choice.AllClasses || s.ClassId == choice.ClassId)).Select(s => s.Round).DefaultIfEmpty(0).Max()+1;
        }
        void UpdateMode()
        {
            if (mode.SelectedValue is BuildMode.Mains)
                sort.ItemsSource = Enum.GetValues<MainSortOrder>().Select(v => new GeneratorSort(v,v.ToDisplay())).ToList();
            else
                sort.ItemsSource = Enum.GetValues<QualificationOrder>().Select(v => new GeneratorSort(v,v == QualificationOrder.PreviousRound ? "Previous Round Results" : v.ToDisplay())).ToList();
            sort.SelectedIndex = 0;
            if (mode.SelectedValue is BuildMode.Mains) sort.SelectedValue = MainSortOrder.QualifyingRank;
            else sort.SelectedValue = QualificationOrder.Random;
            round.Text = mode.SelectedValue is BuildMode.Mains ? "Mains" : NextRound().ToString();
        }
        mode.SelectionChanged += (_, _) => UpdateMode();
        cls.SelectionChanged += (_, _) => round.Text = mode.SelectedValue is BuildMode.Mains ? "Mains" : NextRound().ToString();
        Field(0,"Build",mode); Field(1,"Class",cls); Field(2,"Round #",round); Field(3,"Sort",sort); panel.Children.Add(grid);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,18,0,0) };
        var cancel = new Button { Content = "CANCEL", IsCancel = true };
        var generate = new Button { Content = "GENERATE", IsDefault = true, Padding = new Thickness(22,8,22,8) };
        generate.SetResourceReference(BackgroundProperty,"AccentBrush"); generate.Foreground = System.Windows.Media.Brushes.White;
        generate.Click += (_, _) => {
            if (cls.SelectedItem is null || _raceEvent.ClassIds.Count == 0) { MessageBox.Show(window,"Add a class to the event before generating a round."); return; }
            if (NextRound() > 99 && mode.SelectedValue is BuildMode.QualifyingRound) { MessageBox.Show(window,"The maximum qualifying round is 99."); return; }
            window.DialogResult = true;
        };
        buttons.Children.Add(cancel); buttons.Children.Add(generate); panel.Children.Add(buttons); window.Content = panel; UpdateMode();
        if (window.ShowDialog() != true) return;
        _loadingOptions = true;
        try
        {
            ModeCombo.SelectedValue = mode.SelectedValue;
            ClassCombo.SelectedItem = cls.SelectedItem;
            ConfigureSortOptions(CurrentMode); SortCombo.SelectedValue = sort.SelectedValue;
            RoundText.Text = NextRound().ToString();
        }
        finally { _loadingOptions = false; }
        _browsingSavedLineups = false; _editingExistingScope = false; _lineupSnapshot = null;
        ClassCombo.IsEnabled = ModeCombo.IsEnabled = SortPanel.IsEnabled = true;
        BuildOptionsPanel.Visibility = Visibility.Collapsed; GenerateButton.Visibility = Visibility.Collapsed;
        SaveButton.Content = "SAVE ROUND / MAINS";
        UpdateModeSpecificControls(); GenerateFromRoster(force: true); RememberLineup();
        BuilderStatusText.Text = $"Generated {HeatPlans.Count} race(s). Review the lineup below and SAVE ROUND / MAINS to keep it.";
    }
}
