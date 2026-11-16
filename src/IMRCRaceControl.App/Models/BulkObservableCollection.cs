using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace IMRCRaceControl.Models;

/// <summary>
/// Replace a class roster in a single UI notification, avoiding hundreds of
/// ListBox redraw/layout passes when switching between large imported classes.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> rows)
    {
        CheckReentrancy();
        var snapshot = rows.ToArray();
        Items.Clear();
        foreach (var row in snapshot) Items.Add(row);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
