using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IMRCRaceControl.Models;

public sealed class DetectedTransponder : INotifyPropertyChanged
{
    public required string TransponderId { get; init; }
    public int Crossings { get; private set; }
    public DateTimeOffset LastSeen { get; private set; }
    public string LastSeenDisplay => LastSeen.LocalDateTime.ToString("HH:mm:ss.fff");
    public string DriverName { get; private set; } = "Unassigned";
    public string Source { get; private set; } = "";
    public string Hits { get; private set; } = "";
    public string Strength { get; private set; } = "";

    public void Observe(DateTimeOffset time, string source, string? driver, int? hits, int? strength)
    {
        Crossings++;
        LastSeen = time;
        Source = source;
        DriverName = driver ?? "Unassigned";
        Hits = hits?.ToString() ?? "—";
        Strength = strength?.ToString() ?? "—";
        OnPropertyChanged(null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
