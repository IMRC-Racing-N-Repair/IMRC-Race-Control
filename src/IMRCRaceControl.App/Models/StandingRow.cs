using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IMRCRaceControl.Models;

public sealed class StandingRow : INotifyPropertyChanged
{
    private int _position;
    private int _carNumber;
    private string _driverName = string.Empty;
    private int _laps;
    private string _clock = "—";
    private string _lastLap = "—";
    private string _bestLap = "—";
    private string _bestLapNumber = "—";
    private string _top2 = "—";
    private string _top3 = "—";
    private string _top5 = "—";
    private string _averageLap = "—";
    private string _average5 = "—";
    private string _average10 = "—";
    private string _average15 = "—";
    private string _consistency = "—";
    private string _pace = "—";
    private string _gapToLeader = "—";
    private string _gapBehind = "—";
    private string _totalTime = "—";
    private string _status = string.Empty;
    private bool _isFastestLap;
    private bool _isNewPersonalBest;
    private bool _isLastPassing;
    private bool _isMinimumLapViolationGlow;
    private bool _isCheckedIn;
    private string _manualActionText = "CHECK IN";

    public int Position { get => _position; set => Set(ref _position, value); }
    public int CarNumber { get => _carNumber; set => Set(ref _carNumber, value); }
    public string DriverName { get => _driverName; set => Set(ref _driverName, value ?? string.Empty); }
    public int Laps { get => _laps; set => Set(ref _laps, value); }
    public string Clock { get => _clock; set => Set(ref _clock, value ?? "—"); }
    public string LastLap { get => _lastLap; set => Set(ref _lastLap, value ?? "—"); }

    public string BestLap
    {
        get => _bestLap;
        set
        {
            if (Set(ref _bestLap, value ?? "—"))
                OnPropertyChanged(nameof(BestLapDisplay));
        }
    }

    public string BestLapNumber
    {
        get => _bestLapNumber;
        set
        {
            if (Set(ref _bestLapNumber, value ?? "—"))
                OnPropertyChanged(nameof(BestLapDisplay));
        }
    }

    public string BestLapDisplay =>
        BestLap == "—" || BestLapNumber == "—"
            ? BestLap
            : $"{BestLap} (#{BestLapNumber})";

    public string Top2 { get => _top2; set => Set(ref _top2, value ?? "—"); }
    public string Top3 { get => _top3; set => Set(ref _top3, value ?? "—"); }
    public string Top5 { get => _top5; set => Set(ref _top5, value ?? "—"); }
    public string AverageLap { get => _averageLap; set => Set(ref _averageLap, value ?? "—"); }
    public string Average5 { get => _average5; set => Set(ref _average5, value ?? "—"); }
    public string Average10 { get => _average10; set => Set(ref _average10, value ?? "—"); }
    public string Average15 { get => _average15; set => Set(ref _average15, value ?? "—"); }
    public string Consistency { get => _consistency; set => Set(ref _consistency, value ?? "—"); }
    public string Pace { get => _pace; set => Set(ref _pace, value ?? "—"); }
    public string GapToLeader { get => _gapToLeader; set => Set(ref _gapToLeader, value ?? "—"); }
    public string GapBehind { get => _gapBehind; set => Set(ref _gapBehind, value ?? "—"); }
    public string TotalTime { get => _totalTime; set => Set(ref _totalTime, value ?? "—"); }
    public string Status { get => _status; set { if (Set(ref _status, value ?? string.Empty)) OnPropertyChanged(nameof(IsFinished)); } }
    public bool IsFinished => Status == "FINISHED";
    public bool IsFastestLap { get => _isFastestLap; set => Set(ref _isFastestLap, value); }
    public bool IsNewPersonalBest { get => _isNewPersonalBest; set => Set(ref _isNewPersonalBest, value); }
    public bool IsLastPassing { get => _isLastPassing; set => Set(ref _isLastPassing, value); }
    public bool IsMinimumLapViolationGlow { get => _isMinimumLapViolationGlow; set => Set(ref _isMinimumLapViolationGlow, value); }
    public bool IsCheckedIn { get => _isCheckedIn; set => Set(ref _isCheckedIn, value); }
    public string ManualActionText { get => _manualActionText; set => Set(ref _manualActionText, value ?? "PASS"); }

    public void CopyTimingFrom(StandingRow source)
    {
        Position = source.Position;
        CarNumber = source.CarNumber;
        DriverName = source.DriverName;
        Laps = source.Laps;
        Clock = source.Clock;
        LastLap = source.LastLap;
        BestLap = source.BestLap;
        BestLapNumber = source.BestLapNumber;
        Top2 = source.Top2;
        Top3 = source.Top3;
        Top5 = source.Top5;
        AverageLap = source.AverageLap;
        Average5 = source.Average5;
        Average10 = source.Average10;
        Average15 = source.Average15;
        Consistency = source.Consistency;
        Pace = source.Pace;
        GapToLeader = source.GapToLeader;
        GapBehind = source.GapBehind;
        TotalTime = source.TotalTime;
        Status = source.Status;
        IsFastestLap = source.IsFastestLap;
        IsNewPersonalBest = source.IsNewPersonalBest;
        ManualActionText = source.ManualActionText;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
