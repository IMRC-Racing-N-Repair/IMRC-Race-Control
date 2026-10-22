using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IMRCRaceControl.Models;

public sealed class EventDriverChoice : INotifyPropertyChanged
{
    private bool _included;
    private string _transponderId = string.Empty;
    private bool _isEditable = true;

    public string DriverId { get; init; } = string.Empty;
    public string DriverName { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Nickname { get; init; } = string.Empty;
    public string AssignmentId { get; set; } = string.Empty;

    public bool Included
    {
        get => _included;
        set
        {
            if (_included == value) return;
            _included = value;
            OnPropertyChanged();
        }
    }

    public string TransponderId
    {
        get => _transponderId;
        set
        {
            if (_transponderId == value) return;
            _transponderId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TransponderDisplay));
        }
    }


    public bool IsEditable
    {
        get => _isEditable;
        set
        {
            if (_isEditable == value) return;
            _isEditable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsReadOnly));
        }
    }

    public bool IsReadOnly => !IsEditable;

    public string FullName
    {
        get
        {
            var full = $"{FirstName} {LastName}".Trim();
            return string.IsNullOrWhiteSpace(full) ? DriverName : full;
        }
    }

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Nickname)
            ? Nickname
            : FullName;

    public string SecondaryText
    {
        get
        {
            var full = $"{FirstName} {LastName}".Trim();
            if (!string.IsNullOrWhiteSpace(full) && !string.Equals(full, DisplayName, StringComparison.OrdinalIgnoreCase))
                return full;
            if (!string.IsNullOrWhiteSpace(DriverName) && !string.Equals(DriverName, DisplayName, StringComparison.OrdinalIgnoreCase))
                return DriverName;
            return Included ? "Added to this class" : "Available driver";
        }
    }

    public string TransponderDisplay => string.IsNullOrWhiteSpace(TransponderId) ? "No transponder" : TransponderId;

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        query = query.Trim();
        return DriverName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               FirstName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               LastName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               Nickname.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               TransponderId.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
