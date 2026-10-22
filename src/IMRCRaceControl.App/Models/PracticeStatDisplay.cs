using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IMRCRaceControl.Models;

public sealed class PracticeStatDisplay : INotifyPropertyChanged
{
    private string _value = "—";

    public string Label { get; init; } = string.Empty;

    public string Value
    {
        get => _value;
        set
        {
            value ??= "—";
            if (_value == value) return;
            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
