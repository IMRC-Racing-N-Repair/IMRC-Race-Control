using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IMRCRaceControl.Models;

public sealed class RaceSeriesDefinition : INotifyPropertyChanged
{
    private string _name = "New Series";
    private DateTime _startDate = DateTime.Today;
    private DateTime _endDate = DateTime.Today;
    private string _pointsCsv = DefaultPointsCsv;

    public const string DefaultPointsCsv =
        "100,99,98,97,96,95,94,93,92,91,90,89,88,87,86,85,84,83,82,81";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set { value ??= string.Empty; if (_name == value) return; _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
    }

    public DateTime StartDate
    {
        get => _startDate;
        set { if (_startDate == value) return; _startDate = value.Date; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
    }

    public DateTime EndDate
    {
        get => _endDate;
        set { if (_endDate == value) return; _endDate = value.Date; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
    }

    public string PointsCsv
    {
        get => _pointsCsv;
        set { value ??= string.Empty; if (_pointsCsv == value) return; _pointsCsv = value; OnPropertyChanged(); }
    }

    public int MaximumEvents { get; set; } // 0 = unlimited
    public int BestEventCount { get; set; } // 0 = count all
    public string Notes { get; set; } = string.Empty;
    public SeriesPointsSettings? PointsSettings { get; set; }
    public List<SeriesPointsAdjustment> Adjustments { get; set; } = [];

    // Preserve the points schedule of series created before v0.13.
    public SeriesPointsSettings EffectivePoints => PointsSettings ?? new SeriesPointsSettings {
        Mains = new StagePointsSettings { Enabled = true, PositionPoints = PointsCsv },
        LegacyQualifyingFallback = true };

    public List<string> EventIds { get; set; } = [];

    public string DisplayName => $"{Name} • {StartDate:MM/dd/yyyy}–{EndDate:MM/dd/yyyy}";

    public IReadOnlyList<int> ParsePoints()
    {
        var values = PointsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var points) && points >= 0 ? points : -1)
            .Where(points => points >= 0)
            .ToList();

        return values.Count > 0 ? values : [100, 99, 98, 97, 96];
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
