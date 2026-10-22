using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;
using System.Windows;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private readonly LapSpeechService _lapSpeech = new();

    private bool AnnounceRaceLap(LapRecord lap)
    {
        if (!_settings.LapAnnouncementsEnabled) return false;
        return AnnounceLapTime(lap.LapTime);
    }

    private bool AnnounceLapTime(TimeSpan lapTime)
    {
        var time = LapAnnouncementFormatter.Format(lapTime, _settings.LapAnnouncementsTenthsOnly);
        if (string.IsNullOrEmpty(time)) return false;
        return _lapSpeech.EnqueueLap(time, _settings.LapSpeechRate, _settings.LapSpeechVolume, _settings.LapSpeechVoice,
            _settings.LapSpeechBusyMode, _settings.LapSpeechMaxAgeMilliseconds);
    }

    private void AnnounceDriverCheckIn(int carNumber)
    {
        if (!_settings.AnnounceCheckInNames) return;
        var entrant = SessionRacers.FirstOrDefault(r => r.Enabled && r.CarNumber == carNumber);
        if (entrant is null) return;
        var driver = Drivers.FirstOrDefault(d => d.Id == entrant.DriverId);
        var name = driver?.SpokenName ?? entrant.DisplayName;
        AnnounceCheckInName(name);
    }

    private void AnnounceCheckInName(string name)
    {
        if (_settings.AnnounceCheckInNames)
            _lapSpeech.Enqueue(name, _settings.NameSpeechRate, _settings.LapSpeechVolume, _settings.LapSpeechVoice);
    }

    private void ApplyLapSpeechControls()
    {
        RaceLapCalloutsCheck.IsChecked = _settings.LapAnnouncementsEnabled;
        PracticeLapCalloutsCheck.IsChecked = _settings.PracticeLapCalloutsEnabled;
    }

    private async void RaceLapCallouts_Click(object sender, RoutedEventArgs e)
    {
        // Freeze the inherited practice preference before changing the race switch.
        _settings.PracticeLapAnnouncementsEnabled ??= _settings.LapAnnouncementsEnabled;
        _settings.LapAnnouncementsEnabled = RaceLapCalloutsCheck.IsChecked == true;
        if (!_practiceActive && !_settings.LapAnnouncementsEnabled) _lapSpeech.StopLaps();
        ApplyLapSpeechControls();
        await CaptureAndSaveSettingsAsync();
    }

    private async void PracticeLapCallouts_Click(object sender, RoutedEventArgs e)
    {
        _settings.PracticeLapAnnouncementsEnabled = PracticeLapCalloutsCheck.IsChecked == true;
        if (_practiceActive && !_settings.PracticeLapCalloutsEnabled) _lapSpeech.StopLaps();
        await CaptureAndSaveSettingsAsync();
    }
}
