using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IMRCRaceControl.Models;

public sealed class RacerEntry : INotifyPropertyChanged
{
    private bool _enabled = true;
    private string _driverName = string.Empty;
    private string _nickname = string.Empty;
    private string _transponderId = string.Empty;

    public int CarNumber { get; set; }
    public string DriverId { get; set; } = string.Empty;
    public string ClassId { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string ManualKey => CarNumber <= 9 ? CarNumber.ToString() : $"Alt+{CarNumber - 10}";

    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }

    public string DriverName
    {
        get => _driverName;
        set
        {
            if (SetField(ref _driverName, value ?? string.Empty))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }

    public string Nickname
    {
        get => _nickname;
        set
        {
            if (SetField(ref _nickname, value ?? string.Empty))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Nickname) ? DriverName : Nickname;
    public string ManualButtonLabel => $"{CarNumber}\n{DisplayName}\n{ManualKey}";

    public string TransponderId
    {
        get => _transponderId;
        set => SetField(ref _transponderId, value ?? string.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
