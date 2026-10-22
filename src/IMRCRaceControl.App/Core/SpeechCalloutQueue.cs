using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

public sealed record SpeechCallout(string Text, int Rate, int Volume, long QueuedAt,
    bool IsLap, string Voice, int MaxAgeMilliseconds = 500);

/// <summary>One waiting lap at most. Name check-ins have a separate bounded queue.</summary>
public sealed class SpeechCalloutQueue
{
    private readonly Queue<SpeechCallout> _names = new();
    private SpeechCallout? _lap;
    public int PendingLaps => _lap is null ? 0 : 1;
    public int SkippedLaps { get; private set; }

    public void Enqueue(SpeechCallout callout, bool isSpeaking, LapSpeechBusyMode mode)
    {
        if (!callout.IsLap)
        {
            while (_names.Count >= 64) _names.Dequeue();
            _names.Enqueue(callout);
            return;
        }

        if (mode == LapSpeechBusyMode.SkipWhenBusy)
        {
            ClearLaps();
            if (isSpeaking || _names.Count > 0)
            {
                SkippedLaps++;
                return;
            }
        }

        if (_lap is not null) SkippedLaps++;
        _lap = callout;
    }

    public bool TryDequeue(long now, out SpeechCallout callout)
    {
        if (_names.TryDequeue(out var name)) { callout = name; return true; }
        if (_lap is not null)
        {
            var lap = _lap;
            _lap = null;
            if (now - lap.QueuedAt <= lap.MaxAgeMilliseconds)
            {
                callout = lap;
                return true;
            }
            SkippedLaps++;
        }
        callout = null!;
        return false;
    }

    public void ClearLaps()
    {
        if (_lap is not null) SkippedLaps++;
        _lap = null;
    }

    public void Clear() { ClearLaps(); _names.Clear(); }
    public void ResetStatistics() => SkippedLaps = 0;
}
