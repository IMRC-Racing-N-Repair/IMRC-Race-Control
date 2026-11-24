using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private bool _eventSessionColumnFitPending;

    private void PlannedSessionGrid_Loaded(object sender, RoutedEventArgs e) => QueueEventSessionColumnFit();
    private void PlannedSessionGrid_SizeChanged(object sender, SizeChangedEventArgs e) => QueueEventSessionColumnFit();
    private void PlannedSessionGrid_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) QueueEventSessionColumnFit();
    }

    private void QueueEventSessionColumnFit()
    {
        if (_eventSessionColumnFitPending || PlannedSessionGrid is null) return;
        _eventSessionColumnFitPending = true;
        // Do not compete with input, race timing or the layout pass that raised SizeChanged.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _eventSessionColumnFitPending = false;
            FitEventSessionColumns();
        }));
    }

    private void FitEventSessionColumns()
    {
        var grid = PlannedSessionGrid;
        if (!grid.IsVisible || !double.IsFinite(grid.ActualWidth) || grid.ActualWidth <= 0) return;

        // Use the outer width, not the scroll-content viewport: changing column
        // widths changes that viewport, which can otherwise feed back into
        // another width calculation indefinitely. Reserve the scrollbar once.
        var available = Math.Floor(grid.ActualWidth - grid.BorderThickness.Left
            - grid.BorderThickness.Right - SystemParameters.VerticalScrollBarWidth - 2);
        var fixedWidth = grid.Columns.Where(c => c != EventSessionClassColumn && c != EventSessionNameColumn
            && c.Visibility == Visibility.Visible).Sum(c => c.Width.IsAbsolute ? c.Width.Value : c.ActualWidth);
        var remaining = Math.Floor(available - fixedWidth);
        if (!double.IsFinite(remaining) ||
            remaining < EventSessionClassColumn.MinWidth + EventSessionNameColumn.MinWidth) return;

        var classWidth = Math.Clamp(Math.Floor(remaining * 3 / 7),
            EventSessionClassColumn.MinWidth, remaining - EventSessionNameColumn.MinWidth);
        SetWidth(EventSessionClassColumn, classWidth);
        SetWidth(EventSessionNameColumn, remaining - classWidth);

        static void SetWidth(DataGridColumn column, double pixels)
        {
            if (!column.Width.IsAbsolute || Math.Abs(column.Width.Value - pixels) >= 1)
                column.Width = new DataGridLength(pixels);
        }
    }
}
