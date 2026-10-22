using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IMRCRaceControl.Models;

public sealed class RaceDayEventDefinition : INotifyPropertyChanged
{
    private string _name = "Race Day";
    private DateTime _eventDate = DateTime.Today;
    private EventStatus _status = EventStatus.Active;

    // Null for events created before track information was configured.
    public TrackProfile? Track { get; set; }

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set
        {
            value ??= string.Empty;
            if (_name == value) return;
            _name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public DateTime EventDate
    {
        get => _eventDate;
        set
        {
            if (_eventDate == value) return;
            _eventDate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }

    public EventStatus Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusDisplay));
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public QualificationOrder QualificationOrder { get; set; } = QualificationOrder.Random;
    public List<string> ClassIds { get; set; } = [];
    public List<EventDriverAssignment> DriverAssignments { get; set; } = [];
    public List<EventSessionDefinition> Sessions { get; set; } = [];
    public List<EventRankingEntry> Rankings { get; set; } = [];
    public string LastArchivePath { get; set; } = string.Empty;

    public string StatusDisplay => Status.ToDisplay();
    public string DisplayName => $"{EventDate:MM/dd/yyyy} • {Name} • {StatusDisplay}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
