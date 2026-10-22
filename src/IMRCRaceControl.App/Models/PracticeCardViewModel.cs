using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IMRCRaceControl.Models;

public sealed class PracticeCardViewModel : INotifyPropertyChanged
{
    private DateTimeOffset _lastCrossingAt;
    private bool _isSaving;
    private bool _isLastPassing;
    public bool IsLastPassing { get => _isLastPassing; set { if (_isLastPassing == value) return; _isLastPassing = value; OnPropertyChanged(); } }
    private IReadOnlyList<string> _visibleStatIds = PracticeStatCatalog.Defaults;

    public string StintId { get; init; } = Guid.NewGuid().ToString("N");
    public string DriverId { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string TransponderId { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }

    public void ResolveDriver(string id, string name)
    {
        DriverId = id;
        DriverName = name;
        OnPropertyChanged(nameof(DriverId));
        OnPropertyChanged(nameof(DriverName));
    }

    public DateTimeOffset LastCrossingAt
    {
        get => _lastCrossingAt;
        set
        {
            if (_lastCrossingAt == value) return;
            _lastCrossingAt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(InactivityDisplay));
            RefreshVisibleStats();
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        set { if (_isSaving == value) return; _isSaving = value; OnPropertyChanged(); }
    }

    public ObservableCollection<PracticeLapEntry> Laps { get; } = new();
    public ObservableCollection<PracticeStatDisplay> VisibleStats { get; } = new();

    public int LapCount => Laps.Count;
    public string LastLapDisplay => Laps.Count == 0 ? "—" : Laps[^1].LapTimeDisplay;
    public string BestLapDisplay =>
        Laps.Count == 0
            ? "—"
            : Format(Laps.OrderBy(l => l.LapTime).First().LapTime);

    public string AverageLapDisplay =>
        Laps.Count == 0 ? "—" : Format(AverageLap);

    public TimeSpan AverageLap =>
        Laps.Count == 0
            ? TimeSpan.Zero
            : TimeSpan.FromTicks((long)Laps.Average(l => l.LapTime.Ticks));

    public TimeSpan InactivityTimeout
    {
        get
        {
            if (Laps.Count == 0)
                return TimeSpan.FromSeconds(60);

            var seconds = Math.Max(5.0, AverageLap.TotalSeconds * 10.0);
            return TimeSpan.FromSeconds(seconds);
        }
    }

    public string InactivityDisplay => $"Hide after {InactivityTimeout.TotalSeconds:0}s idle";

    public void SetVisibleStats(IEnumerable<string>? ids)
    {
        _visibleStatIds = (ids ?? PracticeStatCatalog.Defaults)
            .Where(id => PracticeStatCatalog.All.Contains(id, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        RefreshVisibleStats();
    }

    public void AddLap(PracticeLapEntry lap)
    {
        Laps.Add(lap);
        OnPropertyChanged(nameof(LapCount));
        OnPropertyChanged(nameof(LastLapDisplay));
        OnPropertyChanged(nameof(BestLapDisplay));
        OnPropertyChanged(nameof(AverageLapDisplay));
        OnPropertyChanged(nameof(AverageLap));
        OnPropertyChanged(nameof(InactivityTimeout));
        OnPropertyChanged(nameof(InactivityDisplay));
        RefreshVisibleStats();
    }

    private void RefreshVisibleStats()
    {
        var sameLayout =
            VisibleStats.Count == _visibleStatIds.Count &&
            VisibleStats.Select(s => s.Label)
                .SequenceEqual(_visibleStatIds, StringComparer.OrdinalIgnoreCase);

        if (!sameLayout)
        {
            VisibleStats.Clear();
            foreach (var id in _visibleStatIds)
            {
                VisibleStats.Add(new PracticeStatDisplay
                {
                    Label = id,
                    Value = GetStatValue(id)
                });
            }
            return;
        }

        for (var i = 0; i < VisibleStats.Count; i++)
            VisibleStats[i].Value = GetStatValue(_visibleStatIds[i]);
    }

    private string GetStatValue(string id) => id switch
    {
        PracticeStatCatalog.Top2Consecutive => BestConsecutive(2),
        PracticeStatCatalog.Top3Consecutive => BestConsecutive(3),
        PracticeStatCatalog.Top5Consecutive => BestConsecutive(5),
        PracticeStatCatalog.AvgTop5 => AverageFastest(5),
        PracticeStatCatalog.Recent5 => RecentAverage(5),
        PracticeStatCatalog.Consistency => Consistency(),
        PracticeStatCatalog.LastLap => LastLapDisplay,
        PracticeStatCatalog.BestLap => BestLapDisplay,
        PracticeStatCatalog.AverageLap => AverageLapDisplay,
        PracticeStatCatalog.LapCount => LapCount.ToString(),
        PracticeStatCatalog.StintTime => FormatElapsed(LastCrossingAt - StartedAt),
        _ => "—"
    };

    private string BestConsecutive(int count)
    {
        if (Laps.Count < count)
            return "—";

        long bestTicks = long.MaxValue;
        var bestStart = 0;

        for (var i = 0; i <= Laps.Count - count; i++)
        {
            long ticks = 0;
            for (var j = 0; j < count; j++)
                ticks += Laps[i + j].LapTime.Ticks;

            if (ticks < bestTicks)
            {
                bestTicks = ticks;
                bestStart = i;
            }
        }

        var avg = TimeSpan.FromTicks(bestTicks / count);
        var first = Laps[bestStart].LapNumber;
        var last = Laps[bestStart + count - 1].LapNumber;
        return $"{Format(avg)} L{first}-{last}";
    }

    private string AverageFastest(int count)
    {
        if (Laps.Count < count)
            return "—";

        var fastest = Laps
            .OrderBy(l => l.LapTime)
            .Take(count)
            .Select(l => l.LapTime.Ticks)
            .ToList();

        return Format(TimeSpan.FromTicks((long)fastest.Average()));
    }

    private string RecentAverage(int count)
    {
        if (Laps.Count < count)
            return "—";

        var recent = Laps.Skip(Laps.Count - count).Select(l => l.LapTime.Ticks).ToList();
        return Format(TimeSpan.FromTicks((long)recent.Average()));
    }

    private string Consistency()
    {
        if (Laps.Count < 2)
            return "—";

        var values = Laps.Select(l => l.LapTime.TotalSeconds).ToArray();
        var avg = values.Average();
        if (avg <= 0)
            return "—";

        var variance = values.Average(v => Math.Pow(v - avg, 2));
        var stdDev = Math.Sqrt(variance);
        var consistency = Math.Clamp(100.0 - (stdDev / avg * 100.0), 0.0, 100.0);
        return $"{consistency:0.0}%";
    }

    private static string Format(TimeSpan value) =>
        value.TotalMinutes >= 1
            ? $"{(int)value.TotalMinutes}:{value.Seconds:00}.{value.Milliseconds:000}"
            : $"{value.TotalSeconds:0.000}";

    private static string FormatElapsed(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;

        return $"{(int)value.TotalMinutes}:{value.Seconds:00}.{value.Milliseconds:000}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
