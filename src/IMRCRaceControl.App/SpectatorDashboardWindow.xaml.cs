using System.Windows;
using System.Windows.Threading;

namespace IMRCRaceControl;

public partial class SpectatorDashboardWindow : Window
{
    private readonly MainWindow _raceControl;
    private readonly DispatcherTimer _refreshTimer;

    public SpectatorDashboardWindow(MainWindow raceControl)
    {
        InitializeComponent();

        _raceControl = raceControl;
        PopoutZoomSlider.Value = _raceControl.PopoutZoom * 100;
        SpectatorNextRaceStrip.PreviewProvider = _raceControl.GetNextRacePreview;
        SpectatorStandingsGrid.ItemsSource = _raceControl.SpectatorStandings;
        SpectatorProjectionGrid.ItemsSource = _raceControl.SpectatorRoundPositions;
        SpectatorPracticeCards.DataContext = _raceControl;
        SpectatorPracticeCards.ItemsSource = _raceControl.VisiblePracticeCards;

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _refreshTimer.Tick += (_, _) => RefreshHeader();

        Loaded += (_, _) =>
        {
            RefreshHeader();
            _refreshTimer.Start();
        };

        Closed += (_, _) => _refreshTimer.Stop();
    }

    private void PopoutZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_raceControl is null || !IsLoaded) return;
        _raceControl.SetPopoutZoom(e.NewValue);
        RefreshHeader();
    }
    private void PopoutHeaderZoom_Click(object sender, RoutedEventArgs e) { _raceControl.SetPopoutHeaderZoom(PopoutHeaderZoom.IsChecked == true); RefreshHeader(); }
    private void PopoutZoomSliderOut_Click(object sender, RoutedEventArgs e) => PopoutZoomSlider.Value -= 5;
    private void PopoutZoomSliderIn_Click(object sender, RoutedEventArgs e) => PopoutZoomSlider.Value += 5;

    private void RefreshHeader()
    {
        SpectatorStandingsGrid.ItemsSource = _raceControl.SpectatorStandings;
        SpectatorProjectionGrid.ItemsSource = _raceControl.SpectatorRoundPositions;
        PopoutScreenRoot.LayoutTransform = System.Windows.Media.Transform.Identity;
        var z = _raceControl.PopoutZoom;
        SpectatorStandingsGrid.LayoutTransform = new System.Windows.Media.ScaleTransform(z,z);
        SpectatorProjectionGrid.LayoutTransform = new System.Windows.Media.ScaleTransform(z,z);
        SpectatorPracticeCards.LayoutTransform = new System.Windows.Media.ScaleTransform(z,z);
        var hz = _raceControl.IncludePopoutHeader ? z : 1;
        SpectatorHeader.LayoutTransform = new System.Windows.Media.ScaleTransform(hz,hz);
        PopoutHeaderZoom.IsChecked = _raceControl.IncludePopoutHeader;
        SpectatorHeader.Visibility = _raceControl.ShowSpectatorHeader || _raceControl.ShowSpectatorClock ? Visibility.Visible : Visibility.Collapsed;
        RaceHeaderText.Visibility = RaceSubheaderText.Visibility = _raceControl.ShowSpectatorHeader ? Visibility.Visible : Visibility.Collapsed;
        SpectatorNextRaceStrip.ShowStrip = _raceControl.ShowSpectatorNext;
        SpectatorOverallColumn.Width = new GridLength(_raceControl.ShowSpectatorOverall ? 390 : 0);
        var selectedColumns = _raceControl.SpectatorColumns;
        foreach (var col in SpectatorStandingsGrid.Columns)
        {
            var id = col.Header?.ToString() ?? "";
            if (id == "LAST") id = "LAST LAP";
            if (id == "BEST") id = "BEST LAP";
            col.Visibility = selectedColumns.Contains(id) ? Visibility.Visible : Visibility.Collapsed;
            if (selectedColumns.Contains(id)) col.DisplayIndex = selectedColumns.ToList().IndexOf(id);
        }
        if (PopoutZoomSlider.Value != _raceControl.PopoutZoom * 100) PopoutZoomSlider.Value = _raceControl.PopoutZoom * 100;
        var practice = _raceControl.SpectatorIsPractice;
        RaceBoard.Visibility = practice ? Visibility.Collapsed : Visibility.Visible;
        SpectatorPracticeCards.Visibility = practice ? Visibility.Visible : Visibility.Collapsed;
        PracticeEmptyText.Visibility = practice && _raceControl.VisiblePracticeCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClockCaptionText.Visibility = MasterClockText.Visibility = practice || !_raceControl.ShowSpectatorClock ? Visibility.Collapsed : Visibility.Visible;
        if (practice)
        {
            RaceHeaderText.Text = "PRACTICE";
            RaceSubheaderText.Text = $"{_raceControl.VisiblePracticeCards.Count} active drivers";
            return;
        }
        RaceHeaderText.Text = _raceControl.SpectatorRaceHeader;
        RaceSubheaderText.Text = _raceControl.SpectatorRaceSubheader;
        MasterClockText.Text = _raceControl.SpectatorMasterClock;
        ProjectionInfoText.Text = _raceControl.SpectatorProjectionText;
    }
}
