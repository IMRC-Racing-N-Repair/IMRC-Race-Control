using System.Windows;
using System.Windows.Threading;
namespace IMRCRaceControl;
public partial class MainWindow
{
    private DispatcherTimer? _zoomSaveTimer;
    private void ScheduleZoomSave()
    {
        if (_zoomSaveTimer is null)
        {
            _zoomSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _zoomSaveTimer.Tick += async (_, _) => { _zoomSaveTimer.Stop(); await CaptureAndSaveSettingsAsync(); };
        }
        _zoomSaveTimer.Stop(); _zoomSaveTimer.Start();
    }
    private void DashboardZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        _settings.MainScreenZoom = e.NewValue;
        ApplyScreenZoom(); ScheduleZoomSave();
    }
    private void DashboardZoomOut_Click(object sender, RoutedEventArgs e) => DashboardZoom.Value -= 5;
    private void DashboardZoomIn_Click(object sender, RoutedEventArgs e) => DashboardZoom.Value += 5;
    private void DashboardHeaderZoom_Click(object sender, RoutedEventArgs e)
    {
        _settings.MainZoomHeader = DashboardHeaderZoom.IsChecked == true;
        ApplyScreenZoom(); ScheduleZoomSave();
    }
    internal void SetPopoutHeaderZoom(bool value) { _settings.PopoutZoomHeader = value; ScheduleZoomSave(); }
    internal void SetPopoutZoom(double value)
    {
        _settings.PopoutScreenZoom = Math.Clamp(value, 50, 150);
        ScheduleZoomSave();
    }
}
