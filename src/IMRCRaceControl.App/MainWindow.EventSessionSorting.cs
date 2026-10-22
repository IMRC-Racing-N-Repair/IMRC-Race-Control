using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;
public partial class MainWindow
{
    private string _eventSessionSortProperty = nameof(EventSessionDefinition.RaceNumber);
    private ListSortDirection _eventSessionSortDirection = ListSortDirection.Ascending;

    private void PlannedSessionGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        // A column click sorts within each round, never interleaves Q1 with Q2.
        e.Handled = true;
        var property = e.Column.SortMemberPath;
        if (property == nameof(EventSessionDefinition.RoundLabel))
        {
            _eventSessionSortProperty = nameof(EventSessionDefinition.RaceNumber);
            _eventSessionSortDirection = ListSortDirection.Ascending;
        }
        else
        {
            _eventSessionSortDirection = property == _eventSessionSortProperty &&
                _eventSessionSortDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending : ListSortDirection.Ascending;
            _eventSessionSortProperty = property;
        }
        ApplyEventSessionSort();
    }

    private void ApplyEventSessionSort()
    {
        if (PlannedSessionGrid is null) return;
        var view = CollectionViewSource.GetDefaultView(PlannedSessions);
        using (view.DeferRefresh())
        {
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(EventSessionDefinition.RoundGroupHeading)));
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new(nameof(EventSessionDefinition.ScheduleStageOrder), ListSortDirection.Ascending));
            view.SortDescriptions.Add(new(nameof(EventSessionDefinition.ScheduleRoundOrder), ListSortDirection.Ascending));
            view.SortDescriptions.Add(new(_eventSessionSortProperty, _eventSessionSortDirection));
            if (_eventSessionSortProperty != nameof(EventSessionDefinition.RaceNumber))
                view.SortDescriptions.Add(new(nameof(EventSessionDefinition.RaceNumber), ListSortDirection.Ascending));
        }
        foreach (var column in PlannedSessionGrid.Columns)
            column.SortDirection = column.SortMemberPath == _eventSessionSortProperty ? _eventSessionSortDirection : null;
        QueueEventSessionColumnFit();
    }
}
