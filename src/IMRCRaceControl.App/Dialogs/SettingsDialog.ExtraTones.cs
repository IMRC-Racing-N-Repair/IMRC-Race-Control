using System.Windows;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Dialogs;

public partial class SettingsDialog
{
    private readonly AppSettings _extraTones = new();
    private void InitializeExtraTones(AppSettings s, List<Choice<TonePattern>> tones, List<Choice<ToneLength>> lengths)
    {
        SetupCombo(FirstCrossingToneCombo, tones, s.FirstCrossingTone);
        SetupCombo(FirstCrossingLengthCombo, lengths, s.FirstCrossingToneLength);
        FirstCrossingFileText.Text = s.FirstCrossingToneFile;
        SetupCombo(DriverFinishToneCombo, tones, s.DriverFinishTone);
        SetupCombo(DriverFinishLengthCombo, lengths, s.DriverFinishToneLength);
        DriverFinishFileText.Text = s.DriverFinishToneFile;
        SetupCombo(HalfwayToneCombo, tones, s.HalfwayTone);
        SetupCombo(HalfwayLengthCombo, lengths, s.HalfwayToneLength);
        HalfwayFileText.Text = s.HalfwayToneFile;
    }
    private void SaveExtraTones(string[] paths)
    {
        _extraTones.FirstCrossingTone = Selected(FirstCrossingToneCombo, TonePattern.Silent);
        _extraTones.FirstCrossingToneLength = Selected(FirstCrossingLengthCombo, ToneLength.Normal);
        _extraTones.FirstCrossingToneFile = ImportIfSelected(paths[4], "firstcrossing");
        _extraTones.DriverFinishTone = Selected(DriverFinishToneCombo, TonePattern.Silent);
        _extraTones.DriverFinishToneLength = Selected(DriverFinishLengthCombo, ToneLength.Normal);
        _extraTones.DriverFinishToneFile = ImportIfSelected(paths[5], "driverfinish");
        _extraTones.HalfwayTone = Selected(HalfwayToneCombo, TonePattern.Silent);
        _extraTones.HalfwayToneLength = Selected(HalfwayLengthCombo, ToneLength.Normal);
        _extraTones.HalfwayToneFile = ImportIfSelected(paths[6], "halfway");
    }
    private void ApplyExtraTones(AppSettings s)
    {
        s.FirstCrossingTone = _extraTones.FirstCrossingTone;
        s.FirstCrossingToneLength = _extraTones.FirstCrossingToneLength;
        s.FirstCrossingToneFile = _extraTones.FirstCrossingToneFile;
        s.DriverFinishTone = _extraTones.DriverFinishTone;
        s.DriverFinishToneLength = _extraTones.DriverFinishToneLength;
        s.DriverFinishToneFile = _extraTones.DriverFinishToneFile;
        s.HalfwayTone = _extraTones.HalfwayTone;
        s.HalfwayToneLength = _extraTones.HalfwayToneLength;
        s.HalfwayToneFile = _extraTones.HalfwayToneFile;
    }
    private void BrowseFirstCrossing_Click(object sender, RoutedEventArgs e) => BrowseAudio(FirstCrossingFileText);
    private void ClearFirstCrossing_Click(object sender, RoutedEventArgs e) => FirstCrossingFileText.Clear();
    private void TestFirstCrossingTone_Click(object sender, RoutedEventArgs e) =>
        _toneService.Play(Selected(FirstCrossingToneCombo, TonePattern.Silent), Selected(FirstCrossingLengthCombo, ToneLength.Normal), FirstCrossingFileText.Text.Trim());
    private void BrowseDriverFinish_Click(object sender, RoutedEventArgs e) => BrowseAudio(DriverFinishFileText);
    private void ClearDriverFinish_Click(object sender, RoutedEventArgs e) => DriverFinishFileText.Clear();
    private void TestDriverFinishTone_Click(object sender, RoutedEventArgs e) =>
        _toneService.Play(Selected(DriverFinishToneCombo, TonePattern.Silent), Selected(DriverFinishLengthCombo, ToneLength.Normal), DriverFinishFileText.Text.Trim());
    private void BrowseHalfway_Click(object sender, RoutedEventArgs e) => BrowseAudio(HalfwayFileText);
    private void ClearHalfway_Click(object sender, RoutedEventArgs e) => HalfwayFileText.Clear();
    private void TestHalfwayTone_Click(object sender, RoutedEventArgs e) =>
        _toneService.Play(Selected(HalfwayToneCombo, TonePattern.Silent), Selected(HalfwayLengthCombo, ToneLength.Normal), HalfwayFileText.Text.Trim());
}
