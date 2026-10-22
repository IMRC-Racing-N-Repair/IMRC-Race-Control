using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IMRCRaceControl.Models;

public sealed class MainRuleDefinition : INotifyPropertyChanged
{
    private int _order = 1;
    private int _maxRacers = 19;
    private int _bumpInsFromLower;
    private int _runCount = 1;
    private int _countBest = 1;

    public int Order
    {
        get => _order;
        set
        {
            value = Math.Max(1, value);
            if (_order == value) return;
            _order = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MainName));
            OnPropertyChanged(nameof(RuleDisplay));
        }
    }

    public int MaxRacers
    {
        get => _maxRacers;
        set
        {
            value = Math.Clamp(value, 1, 30);
            if (_maxRacers == value) return;
            _maxRacers = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DirectQualifierSlots));
            OnPropertyChanged(nameof(RuleDisplay));
        }
    }

    /// <summary>
    /// Number of racers accepted into this main from the next lower main.
    /// Example: A Main MaxRacers=8 / BumpInsFromLower=2 means A is seeded
    /// with six direct qualifiers and reserves two spots for B-Main bump-ups.
    /// </summary>
    public int BumpInsFromLower
    {
        get => _bumpInsFromLower;
        set
        {
            value = Math.Max(0, value);
            if (_bumpInsFromLower == value) return;
            _bumpInsFromLower = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DirectQualifierSlots));
            OnPropertyChanged(nameof(RuleDisplay));
        }
    }


    /// <summary>
    /// Number of separate races/legs run for this main. IFMAR-style A mains
    /// commonly use three legs.
    /// </summary>
    public int RunCount
    {
        get => _runCount;
        set
        {
            value = Math.Clamp(value, 1, 10);
            if (_runCount == value) return;
            _runCount = value;
            if (_countBest > _runCount)
                _countBest = _runCount;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CountBest));
            OnPropertyChanged(nameof(RuleDisplay));
        }
    }

    /// <summary>
    /// Number of best main-leg finishes used to determine the overall main
    /// result. Example: three A mains / best two count.
    /// </summary>
    public int CountBest
    {
        get => _countBest;
        set
        {
            value = Math.Clamp(value, 1, Math.Max(1, RunCount));
            if (_countBest == value) return;
            _countBest = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RuleDisplay));
        }
    }

    public string MainName =>
        Order is >= 1 and <= 26
            ? $"{(char)('A' + Order - 1)} Main"
            : $"Main {Order}";

    public int DirectQualifierSlots =>
        Math.Max(1, MaxRacers - Math.Min(BumpInsFromLower, Math.Max(0, MaxRacers - 1)));

    public string RuleDisplay
    {
        get
        {
            var fieldRule = BumpInsFromLower > 0
                ? $"{DirectQualifierSlots} direct + {BumpInsFromLower} bump = {MaxRacers}"
                : $"{MaxRacers} racers";

            var runs = RunCount > 1
                ? $" • {RunCount} runs / best {CountBest}"
                : " • 1 run";

            return $"{MainName}: {fieldRule}{runs}";
        }
    }

    public MainRuleDefinition Clone() => new()
    {
        Order = Order,
        MaxRacers = MaxRacers,
        BumpInsFromLower = BumpInsFromLower,
        RunCount = RunCount,
        CountBest = CountBest
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
