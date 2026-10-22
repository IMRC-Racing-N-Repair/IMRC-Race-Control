using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class UnknownTransponderWindow : Window
{
    internal const string DragFormat = "IMRC.UnknownTransponder";
    private readonly MainWindow _raceControl;

    public UnknownTransponderWindow(MainWindow raceControl)
    {
        InitializeComponent();
        _raceControl = raceControl;

        UnknownGrid.ItemsSource = _raceControl.UnknownTransponderHits;
        RacerGrid.ItemsSource = _raceControl.SessionRacers;
    }

    private void UnknownGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            UnknownGrid.SelectedItem is not UnknownTransponderHit hit)
        {
            return;
        }

        DragDrop.DoDragDrop(
            UnknownGrid,
            new DataObject(DragFormat, hit),
            DragDropEffects.Move);
    }

    private void RacerGrid_DragOver(object sender, DragEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        e.Effects = e.Data.GetDataPresent(DragFormat) && row?.Item is RacerEntry
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void RacerGrid_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DragFormat) is not UnknownTransponderHit hit)
            return;

        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        var racer = row?.Item as RacerEntry;
        e.Handled = true;
        if (racer is null)
            return;

        await _raceControl.AssignUnknownTransponderAsync(hit, racer);
    }

    private async void AssignSelected_Click(object sender, RoutedEventArgs e)
    {
        if (UnknownGrid.SelectedItem is not UnknownTransponderHit hit ||
            RacerGrid.SelectedItem is not RacerEntry racer)
        {
            MessageBox.Show(this,
                "Select an unknown transponder and the racer it belongs to.",
                "Assign transponder",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await _raceControl.AssignUnknownTransponderAsync(hit, racer);
    }

    private void CopyNumber_Click(object sender, RoutedEventArgs e)
    {
        if (UnknownGrid.SelectedItem is UnknownTransponderHit hit)
            _raceControl.CopyTransponderNumber(hit.TransponderId);
    }

    private void ClearSelected_Click(object sender, RoutedEventArgs e)
    {
        if (UnknownGrid.SelectedItem is UnknownTransponderHit hit)
            _raceControl.ClearUnknownTransponder(hit);
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (_raceControl.UnknownTransponderHits.Count == 0)
            return;

        if (MessageBox.Show(this,
                "Clear every unassigned transponder crossing from this window?",
                "Clear unknown transponders",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _raceControl.ClearAllUnknownTransponders();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T wanted)
                return wanted;

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
