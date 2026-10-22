using System.Speech.Synthesis;
using System.Windows.Threading;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Services;

/// <summary>UI-dispatcher-owned, asynchronous local Windows speech.</summary>
public sealed class LapSpeechService : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly SpeechCalloutQueue _pending = new();
    private SpeechSynthesizer? _synth;
    private Prompt? _current;
    private bool _currentIsLap;
    private string _defaultVoice = string.Empty;
    private string[] _voiceNames = [];
    private bool _disposed;
    private long _lapStartedAt;
    private long _completedLapMilliseconds;
    private int _completedLapCount;
    public int StartedLapCount { get; private set; }
    public int SkippedLapCount => _pending.SkippedLaps;
    public double AverageLapSpeechMilliseconds => _completedLapCount == 0 ? 0
        : (double)_completedLapMilliseconds / _completedLapCount;
    public void ResetStatistics()
    {
        StartedLapCount = 0;
        _completedLapMilliseconds = 0;
        _completedLapCount = 0;
        _pending.ResetStatistics();
    }
    public bool IsSpeakingAnnouncement => _current is not null && !_currentIsLap;
    public string? LastError { get; private set; }

    public bool Enqueue(string text, int rate = 2, int volume = 100, string voice = "")
        => EnqueueCore(text, rate, volume, isLap: false, voice, LapSpeechBusyMode.LatestOnly, 500);

    public bool EnqueueLap(string time, int rate = 2, int volume = 100, string voice = "",
        LapSpeechBusyMode busyMode = LapSpeechBusyMode.LatestOnly, int maxAgeMilliseconds = 500)
        => EnqueueCore(time, rate, volume, isLap: true, voice, busyMode, maxAgeMilliseconds);

    public IReadOnlyList<string> GetVoiceNames() => EnsureVoice() ? _voiceNames : [];

    private bool EnqueueCore(string text, int rate, int volume, bool isLap, string voice,
        LapSpeechBusyMode busyMode, int maxAgeMilliseconds)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text)) return false;
        if (!EnsureVoice()) return false;
        _pending.Enqueue(new SpeechCallout(text, Math.Clamp(rate, -10, 10), Math.Clamp(volume, 0, 100),
            Environment.TickCount64, isLap, voice, Math.Clamp(maxAgeMilliseconds, 250, 1000)),
            _current is not null, busyMode);
        Pump();
        return LastError is null;
    }

    private bool EnsureVoice()
    {
        if (_synth is not null) return true;
        if (LastError is not null) return false;
        try
        {
            _synth = new SpeechSynthesizer();
            _synth.SetOutputToDefaultAudioDevice();
            _defaultVoice = _synth.Voice.Name;
            _voiceNames = _synth.GetInstalledVoices().Where(v => v.Enabled)
                .Select(v => v.VoiceInfo.Name).Distinct().OrderBy(n => n).ToArray();
            _synth.SpeakCompleted += SpeakCompleted;
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            Fail(ex);
            return false;
        }
    }

    private void Pump()
    {
        if (_disposed || _current is not null || _synth is null) return;
        while (_pending.TryDequeue(Environment.TickCount64, out var callout))
        {
            try
            {
                _synth.Rate = callout.Rate;
                _synth.Volume = callout.Volume;
                var voice = _voiceNames.Contains(callout.Voice, StringComparer.Ordinal)
                    ? callout.Voice : _defaultVoice;
                if (!string.Equals(_synth.Voice.Name, voice, StringComparison.Ordinal))
                    _synth.SelectVoice(voice);
                _currentIsLap = callout.IsLap;
                if (callout.IsLap)
                {
                    // One prompt, separate cardinal numbers, explicitly no
                    // prosodic break: "fourthree", never "forty-three" or "point".
                    var prompt = new PromptBuilder(_synth.Voice.Culture);
                    var parts = callout.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (var i = 0; i < parts.Length; i++)
                    {
                        if (i > 0) prompt.AppendBreak(PromptBreak.None);
                        prompt.AppendTextWithHint(parts[i], SayAs.NumberCardinal);
                    }
                    _lapStartedAt = Environment.TickCount64;
                    _current = _synth.SpeakAsync(prompt);
                    StartedLapCount++;
                }
                else _current = _synth.SpeakAsync(callout.Text);
            }
            catch (Exception ex) { Fail(ex); }
            break;
        }
    }

    private void SpeakCompleted(object? sender, SpeakCompletedEventArgs e)
    {
        if (_dispatcher.HasShutdownStarted) return;
        _ = _dispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed || !ReferenceEquals(_current, e.Prompt)) return;
            if (_currentIsLap && !e.Cancelled && e.Error is null)
            {
                _completedLapMilliseconds += Environment.TickCount64 - _lapStartedAt;
                _completedLapCount++;
            }
            _current = null;
            if (e.Error is not null) Fail(e.Error);
            else Pump();
        }));
    }

    public void ResetFailure() => LastError = null;

    public void StopLaps()
    {
        _pending.ClearLaps();
        if (_current is not null && _currentIsLap)
        {
            _current = null;
            try { _synth?.SpeakAsyncCancelAll(); }
            catch (Exception ex) { Fail(ex); }
        }
        Pump();
    }

    public void Stop()
    {
        _pending.Clear();
        _current = null;
        try { _synth?.SpeakAsyncCancelAll(); }
        catch (Exception ex) { Fail(ex); }
    }

    private void Fail(Exception ex)
    {
        LastError = "Windows speech is unavailable. Check your Windows voice and audio output.";
        _pending.Clear();
        _current = null;
        if (_synth is not null)
        {
            _synth.SpeakCompleted -= SpeakCompleted;
            try { _synth.Dispose(); } catch { }
            _synth = null;
        }
        try { AppCrashLogger.Log("Lap speech", ex); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        if (_synth is not null)
        {
            _synth.SpeakCompleted -= SpeakCompleted;
            try { _synth.Dispose(); } catch { }
            _synth = null;
        }
    }
}
