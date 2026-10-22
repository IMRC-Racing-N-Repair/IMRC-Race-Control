using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IMRCRaceControl.Models;

public sealed class EventClassChoice : INotifyPropertyChanged
{
    private bool _included;
    private int _order;
    private bool _isEditable = true;

    public string ClassId { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public int RacersPerHeat { get; init; }

    public bool Included
    {
        get => _included;
        set
        {
            if (_included == value) return;
            _included = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OrderDisplay));
        }
    }

    public int Order
    {
        get => _order;
        set
        {
            if (_order == value) return;
            _order = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OrderDisplay));
        }
    }

    public string OrderDisplay => Included && Order > 0 ? Order.ToString() : "—";


    public bool IsEditable
    {
        get => _isEditable;
        set
        {
            if (_isEditable == value) return;
            _isEditable = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
