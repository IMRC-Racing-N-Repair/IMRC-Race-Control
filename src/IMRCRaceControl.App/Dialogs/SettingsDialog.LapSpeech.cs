using System.Globalization;
using System.Windows;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;
using System.Windows.Threading;
using BusyMode = IMRCRaceControl.Models.LapSpeechBusyMode;

namespace IMRCRaceControl.Dialogs;

public partial class SettingsDialog
{
    private readonly LapSpeechService _speechPreview = new();
    private readonly DispatcherTimer _busyTrackTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int _busySampleCount;
    private int _busyDrainTicks;
    public bool LapAnnouncementsEnabled => LapSpeechEnabledCheck.IsChecked == true;
    public bool PracticeLapAnnouncementsEnabled => PracticeLapSpeechEnabledCheck.IsChecked == true;
    public string LapSpeechVoice => LapSpeechVoiceCombo.SelectedValue as string ?? string.Empty;
    public bool LapAnnouncementsTenthsOnly => LapSpeechTenthsCheck.IsChecked == true;
    public bool AnnounceCheckInNames => LapSpeechNameCheck.IsChecked == true;
    public int LapSpeechRate => (int)LapSpeechRateSlider.Value;
    public int NameSpeechRate => (int)NameSpeechRateSlider.Value;
    public BusyMode LapSpeechBusyMode => Selected(LapSpeechBusyCombo, BusyMode.LatestOnly);
    public int LapSpeechMaxAgeMilliseconds => (int)LapSpeechAgeSlider.Value;
    public int LapSpeechVolume => (int)LapSpeechVolumeSlider.Value;

    private void InitializeLapSpeech(AppSettings settings)
    {
        LapSpeechEnabledCheck.IsChecked = settings.LapAnnouncementsEnabled;
        PracticeLapSpeechEnabledCheck.IsChecked = settings.PracticeLapCalloutsEnabled;
        var voices = _speechPreview.GetVoiceNames();
        var choices = new List<Choice<string>> { new("", "Windows default voice") };
        choices.AddRange(voices.Select(v => new Choice<string>(v, v)));
        if (!string.IsNullOrWhiteSpace(settings.LapSpeechVoice) && !voices.Contains(settings.LapSpeechVoice))
            choices.Add(new(settings.LapSpeechVoice, $"{settings.LapSpeechVoice} (unavailable — using Windows default)"));
        SetupCombo(LapSpeechVoiceCombo, choices, settings.LapSpeechVoice ?? string.Empty);
        if (_speechPreview.LastError is not null) LapSpeechTestResult.Text = _speechPreview.LastError;
        LapSpeechTenthsCheck.IsChecked = settings.LapAnnouncementsTenthsOnly;
        LapSpeechNameCheck.IsChecked = settings.AnnounceCheckInNames;
        LapSpeechRateSlider.Value = Math.Clamp(settings.LapSpeechRate, -10, 10);
        NameSpeechRateSlider.Value = Math.Clamp(settings.NameSpeechRate, -10, 10);
        LapSpeechAgeSlider.Value = Math.Clamp(settings.LapSpeechMaxAgeMilliseconds, 250, 1000);
        SetupCombo(LapSpeechBusyCombo, new[] {
            new Choice<BusyMode>(BusyMode.LatestOnly, "Latest lap only — keep at most one waiting"),
            new Choice<BusyMode>(BusyMode.SkipWhenBusy, "Skip while speaking — no waiting laps")
        }, settings.LapSpeechBusyMode);
        LapSpeechVolumeSlider.Value = Math.Clamp(settings.LapSpeechVolume, 0, 100);
        _busyTrackTimer.Tick += BusyTrackTimer_Tick;
        Closed += (_, _) => { _busyTrackTimer.Stop(); _speechPreview.Dispose(); };
    }

    private void TestLapSpeech_Click(object sender, RoutedEventArgs e)
    {
        StopBusyTrackTest();
        if (!decimal.TryParse(LapSpeechTestText.Text, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var seconds) || seconds <= 0 || seconds > 86400)
        {
            MessageBox.Show(this, "Enter a lap time between 0 and 86400 seconds, such as 4.339.",
                "Test lap callout");
            return;
        }

        var time = LapAnnouncementFormatter.Format(
            TimeSpan.FromTicks((long)(seconds * TimeSpan.TicksPerSecond)), LapAnnouncementsTenthsOnly);
        LapSpeechTestResult.Text = $"Says: {time} (continuous, no pause)";
        _speechPreview.Stop();
        _speechPreview.ResetFailure();
        if (!_speechPreview.EnqueueLap(time, LapSpeechRate, LapSpeechVolume, LapSpeechVoice,
                LapSpeechBusyMode, LapSpeechMaxAgeMilliseconds))
            LapSpeechTestResult.Text = _speechPreview.LastError ?? "Unable to play the test.";
    }

    private void TestNameSpeed_Click(object sender, RoutedEventArgs e)
    {
        StopBusyTrackTest();
        _speechPreview.Stop();
        _speechPreview.ResetFailure();
        LapSpeechTestResult.Text = "Name-speed test: Driver checked in";
        if (!_speechPreview.Enqueue("Driver checked in", NameSpeechRate, LapSpeechVolume, LapSpeechVoice))
            LapSpeechTestResult.Text = _speechPreview.LastError ?? "Unable to play the test.";
    }

    private void BusyTrackTest_Click(object sender, RoutedEventArgs e)
    {
        if (_busyTrackTimer.IsEnabled) { StopBusyTrackTest(); return; }
        _speechPreview.Stop();
        _speechPreview.ResetFailure();
        _speechPreview.ResetStatistics();
        _busySampleCount = 0;
        _busyDrainTicks = 0;
        BusyTrackTestButton.Content = "STOP BUSY-TRACK TEST";
        _busyTrackTimer.Start();
        AddBusyTrackSample();
    }

    private void BusyTrackTimer_Tick(object? sender, EventArgs e)
    {
        if (_speechPreview.LastError is not null)
        {
            StopBusyTrackTest();
            LapSpeechTestResult.Text = _speechPreview.LastError;
            return;
        }
        if (_busySampleCount < 16) AddBusyTrackSample();
        else if (++_busyDrainTicks >= 3) { StopBusyTrackTest(); return; }
        if (!_busyTrackTimer.IsEnabled) return;
        UpdateBusyTrackSummary("Testing");
    }

    private void AddBusyTrackSample()
    {
        var time = LapAnnouncementFormatter.Format(TimeSpan.FromMilliseconds(4000 + (_busySampleCount % 10) * 37),
            LapAnnouncementsTenthsOnly);
        _busySampleCount++;
        if (!_speechPreview.EnqueueLap(time, LapSpeechRate, LapSpeechVolume, LapSpeechVoice,
                LapSpeechBusyMode, LapSpeechMaxAgeMilliseconds))
        {
            StopBusyTrackTest();
            LapSpeechTestResult.Text = _speechPreview.LastError ?? "Unable to play the test.";
            return;
        }
        UpdateBusyTrackSummary("Testing");
    }

    private void StopBusyTrackTest()
    {
        if (!_busyTrackTimer.IsEnabled) return;
        _busyTrackTimer.Stop();
        _speechPreview.Stop();
        BusyTrackTestButton.Content = "TEST 8 CARS / 4-SECOND LAPS";
        UpdateBusyTrackSummary("Test stopped");
    }

    private void UpdateBusyTrackSummary(string status)
    {
        var average = _speechPreview.AverageLapSpeechMilliseconds;
        var timing = average > 0 ? $" Average completed call: {average:0} ms (target below 500 ms)." : "";
        LapSpeechTestResult.Text = $"{status}: {_busySampleCount} offered, {_speechPreview.StartedLapCount} started, " +
            $"{_speechPreview.SkippedLapCount} skipped.{timing}";
    }
}
