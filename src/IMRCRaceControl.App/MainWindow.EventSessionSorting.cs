using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;
public partial class MainWindow
{
    private string _eventSessionSortProperty = nameof(EventRunRow.RaceNumber);
    private ListSortDirection _eventSessionSortDirection = ListSortDirection.Ascending;

    private void PlannedSessionGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        // A column click sorts within each round, never interleaves Q1 with Q2.
        e.Handled = true;
        var property = e.Column.SortMemberPath;
        if (property == nameof(EventRunRow.RoundLabel))
        {
            _eventSessionSortProperty = nameof(EventRunRow.RaceNumber);
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

    private sealed class EventRunComparer(string property, bool descending) : System.Collections.IComparer
    {
        public int Compare(object? x, object? y) =>
            x is EventRunRow a && y is EventRunRow b ? EventRunRow.Compare(a,b,property,descending) : 0;
    }

    private void ApplyEventSessionSort()
    {
        if (PlannedSessionGrid is null) return;
        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(PlannedSessions);
        using (view.DeferRefresh())
        {
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(EventRunRow.RoundGroupHeading)));
            var property = _eventSessionSortProperty;
            var descending = _eventSessionSortDirection == ListSortDirection.Descending;
            view.CustomSort = new EventRunComparer(property, descending);
        }
        foreach (var column in PlannedSessionGrid.Columns)
            column.SortDirection = column.SortMemberPath == _eventSessionSortProperty ? _eventSessionSortDirection : null;
        QueueEventSessionColumnFit();
    }
}
