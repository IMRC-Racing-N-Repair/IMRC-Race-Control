using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private bool _eventSessionColumnFitPending;

    private void PlannedSessionGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (PlannedSessionGrid.Template.FindName("DG_ScrollViewer", PlannedSessionGrid) is ScrollViewer viewer)
        {
            viewer.ScrollChanged -= EventSessionViewportChanged;
            viewer.ScrollChanged += EventSessionViewportChanged;
        }
        QueueEventSessionColumnFit();
    }

    private void PlannedSessionGrid_SizeChanged(object sender, SizeChangedEventArgs e) => QueueEventSessionColumnFit();

    private void PlannedSessionGrid_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) QueueEventSessionColumnFit();
    }

    private void EventSessionViewportChanged(object sender, ScrollChangedEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource) && e.ViewportWidthChange != 0)
            QueueEventSessionColumnFit();
    }

    private void QueueEventSessionColumnFit()
    {
        if (_eventSessionColumnFitPending || PlannedSessionGrid is null) return;
        _eventSessionColumnFitPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _eventSessionColumnFitPending = false;
            FitEventSessionColumns();
        }));
    }

    private void FitEventSessionColumns()
    {
        var grid = PlannedSessionGrid;
        if (!grid.IsVisible || grid.ActualWidth <= 0) return;

        // Grouped rows can be measured with infinite horizontal space, making
        // star columns shrink to their content. Use the actual viewport in pixels.
        var available = grid.ActualWidth - grid.BorderThickness.Left - grid.BorderThickness.Right
            - SystemParameters.VerticalScrollBarWidth;
        if (grid.Template.FindName("DG_ScrollViewer", grid) is ScrollViewer viewer &&
            viewer.Template.FindName("PART_ScrollContentPresenter", viewer) is ScrollContentPresenter presenter &&
            presenter.ActualWidth > 0)
            available = Math.Min(available + SystemParameters.VerticalScrollBarWidth, presenter.ActualWidth);

        // Leave a pixel for each edge to avoid a scrollbar from layout rounding.
        available -= 2;
        var fixedWidth = grid.Columns.Where(c => c != EventSessionClassColumn && c != EventSessionNameColumn
            && c.Visibility == Visibility.Visible).Sum(c => c.ActualWidth);
        var remaining = available - fixedWidth;
        if (remaining < EventSessionClassColumn.MinWidth + EventSessionNameColumn.MinWidth) return;

        var classWidth = Math.Clamp(Math.Floor(remaining * 3 / 7),
            EventSessionClassColumn.MinWidth, remaining - EventSessionNameColumn.MinWidth);
        EventSessionClassColumn.Width = new DataGridLength(classWidth);
        EventSessionNameColumn.Width = new DataGridLength(remaining - classWidth);
    }
}
