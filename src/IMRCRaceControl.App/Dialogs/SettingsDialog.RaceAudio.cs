using System.Windows;
using IMRCRaceControl.Models;
using IMRCRaceControl.Core;

namespace IMRCRaceControl.Dialogs;

public partial class SettingsDialog
{
    private void InitializeRaceAudio(AppSettings s)
    {
        RaceLeaderCountCombo.ItemsSource = new[] { 1, 2, 3, 4, 5 };
        RaceLeaderCountCombo.SelectedItem = Math.Clamp(s.RaceLeaderCount, 1, 5);
        RaceLeaderIntervalSlider.Value = s.RaceLeadersEnabled ? Math.Clamp(s.RaceLeaderIntervalSeconds, 0, 60) : 0;
        RaceAnnouncementRateSlider.Value = s.RaceAnnouncementRate;
        RaceHalfwayCheck.IsChecked = s.RaceHalfwayToneEnabled;
        RaceCountdownCheck.IsChecked = s.RaceCountdownEnabled;
        RaceCountdownSlider.Value = s.RaceCountdownSeconds;
        RaceTwoMinutesCheck.IsChecked = s.RaceTwoMinutesEnabled;
        RaceOneMinuteCheck.IsChecked = s.RaceOneMinuteEnabled;
        RaceFirstCrossingCheck.IsChecked = s.RaceFirstCrossingToneEnabled;
        RaceDriverFinishCheck.IsChecked = s.RaceDriverFinishToneEnabled;
    }
    public void ApplyRaceAudio(AppSettings s)
    {
        ApplyExtraTones(s);
        s.RaceLeadersEnabled = RaceLeaderIntervalSlider.Value > 0;
        s.RaceLeaderCount = RaceLeaderCountCombo.SelectedItem is int count ? count : 3;
        s.RaceLeaderIntervalSeconds = (int)RaceLeaderIntervalSlider.Value;
        s.RaceAnnouncementRate = (int)RaceAnnouncementRateSlider.Value;
        s.RaceHalfwayToneEnabled = RaceHalfwayCheck.IsChecked == true;
        s.RaceCountdownEnabled = RaceCountdownCheck.IsChecked == true;
        s.RaceCountdownSeconds = (int)RaceCountdownSlider.Value;
        s.RaceTwoMinutesEnabled = RaceTwoMinutesCheck.IsChecked == true;
        s.RaceOneMinuteEnabled = RaceOneMinuteCheck.IsChecked == true;
        s.RaceFirstCrossingToneEnabled = RaceFirstCrossingCheck.IsChecked == true;
        s.RaceDriverFinishToneEnabled = RaceDriverFinishCheck.IsChecked == true;
    }
    private void TestLeaders_Click(object sender, RoutedEventArgs e)
    {
        StopBusyTrackTest();
        _speechPreview.Stop();
        _speechPreview.ResetFailure();
        _speechPreview.Enqueue(RaceAudioSchedule.LeaderText(new[] { "Yoshi", "Alex", "Sam", "Taylor", "Jordan" },
            RaceLeaderCountCombo.SelectedItem is int n ? n : 3), (int)RaceAnnouncementRateSlider.Value, LapSpeechVolume, LapSpeechVoice);
    }
    private void PreviewRaceTime(string text)
    {
        StopBusyTrackTest();
        _speechPreview.Stop();
        _speechPreview.ResetFailure();
        if (!_speechPreview.Enqueue(text, (int)RaceAnnouncementRateSlider.Value, LapSpeechVolume, LapSpeechVoice))
            MessageBox.Show(this, _speechPreview.LastError ?? "Unable to play the callout.", "Audio test");
    }
    private void TestHalfway_Click(object sender, RoutedEventArgs e) => PreviewRaceTime("Halfway");
    private void TestTwoMinutes_Click(object sender, RoutedEventArgs e) => PreviewRaceTime("2 minutes to go");
    private void TestOneMinute_Click(object sender, RoutedEventArgs e) => PreviewRaceTime("1 minute to go");
}
