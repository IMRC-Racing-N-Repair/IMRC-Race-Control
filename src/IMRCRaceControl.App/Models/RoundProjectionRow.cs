using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IMRCRaceControl.Models;

public sealed class RoundProjectionRow : INotifyPropertyChanged
{
    private int? _projectedPosition;
    private string _driverName = string.Empty;
    private string _paceFinish = "—";
    private string _movementState = string.Empty;
    private string _movementDisplay = "—";
    private bool _isCurrentHeat;

    public string Key { get; init; } = string.Empty;
    public string DriverId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;

    public int? ProjectedPosition
    {
        get => _projectedPosition;
        set
        {
            if (_projectedPosition == value) return;
            _projectedPosition = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProjectedPositionDisplay));
        }
    }

    public string ProjectedPositionDisplay => ProjectedPosition?.ToString() ?? "—";

    public string DriverName
    {
        get => _driverName;
        set => Set(ref _driverName, value ?? string.Empty);
    }

    public string PaceFinish
    {
        get => _paceFinish;
        set => Set(ref _paceFinish, value ?? "—");
    }

    // UP / DOWN / SAME / empty (non-current or pending)
    public string MovementState
    {
        get => _movementState;
        set => Set(ref _movementState, value ?? string.Empty);
    }

    public string MovementDisplay
    {
        get => _movementDisplay;
        set => Set(ref _movementDisplay, value ?? "—");
    }

    public bool IsCurrentHeat
    {
        get => _isCurrentHeat;
        set => Set(ref _isCurrentHeat, value);
    }

    public void CopyFrom(RoundProjectionRow source)
    {
        ProjectedPosition = source.ProjectedPosition;
        DriverName = source.DriverName;
        PaceFinish = source.PaceFinish;
        MovementState = source.MovementState;
        MovementDisplay = source.MovementDisplay;
        IsCurrentHeat = source.IsCurrentHeat;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
