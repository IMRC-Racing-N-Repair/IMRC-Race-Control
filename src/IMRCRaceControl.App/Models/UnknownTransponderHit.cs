using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IMRCRaceControl.Models;

public sealed record UnknownTransponderCrossing(
    DateTimeOffset Timestamp,
    string Source);

public sealed class UnknownTransponderHit : INotifyPropertyChanged
{
    private DateTimeOffset _lastSeen;
    private string _lastSource = string.Empty;

    public string TransponderId { get; init; } = string.Empty;
    public DateTimeOffset FirstSeen { get; init; }
    public List<UnknownTransponderCrossing> Crossings { get; } = [];

    public DateTimeOffset LastSeen
    {
        get => _lastSeen;
        private set
        {
            if (_lastSeen == value) return;
            _lastSeen = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LastSeenDisplay));
        }
    }

    public string LastSource
    {
        get => _lastSource;
        private set
        {
            if (_lastSource == value) return;
            _lastSource = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public int Count => Crossings.Count;
    public string LastSeenDisplay => LastSeen.LocalDateTime.ToString("HH:mm:ss.fff");

    public void AddCrossing(DateTimeOffset timestamp, string source)
    {
        Crossings.Add(new UnknownTransponderCrossing(timestamp, source));
        LastSeen = timestamp;
        LastSource = source;
        OnPropertyChanged(nameof(Count));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
