using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IMRCRaceControl.Models;

public sealed class SeriesEventChoice : INotifyPropertyChanged
{
    private bool _included;

    public string EventId { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;
    public DateTime EventDate { get; init; }

    public bool Included
    {
        get => _included;
        set { if (_included == value) return; _included = value; OnPropertyChanged(); }
    }

    public string Status { get; init; } = "";
    public string Display => $"{EventDate:MM/dd/yyyy} • {EventName} • {Status}";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
