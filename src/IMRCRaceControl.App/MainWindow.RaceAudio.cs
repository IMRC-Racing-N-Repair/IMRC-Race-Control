using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private readonly RaceAudioSchedule _raceAudio = new();
    private void UpdateMasterEndTone(DateTimeOffset now)
    {
        if (_recallMode || _endTonePlayed || _raceEngine.PracticeMode || _raceEngine.IsLappedRace ||
            _raceEngine.MasterStartedAt is null ||
            _raceEngine.State is not (RaceState.Armed or RaceState.Running or RaceState.TimeExpired) ||
            _raceEngine.GetRemaining(now) > TimeSpan.Zero) return;
        _endTonePlayed = true;
        _lapSpeech.Stop();
        _toneService.Play(_settings.EndTone, _settings.EndToneLength, _settings.EndToneFile);
    }

    private void UpdateRaceAudio(DateTimeOffset now)
    {
        if (_recallMode || _raceEngine.PracticeMode || _raceEngine.MasterStartedAt is null ||
            _raceEngine.State is not (RaceState.Armed or RaceState.Running or RaceState.TimeExpired)) return;
        var standings = _raceEngine.GetStandings(now);
        var cues = _raceAudio.Advance(_raceEngine.GetMasterElapsed(now).TotalSeconds,
            _raceEngine.Duration.TotalSeconds, _raceEngine.IsLappedRace,
            standings.Select(r => r.Laps).DefaultIfEmpty().Max(), _raceEngine.TargetLaps, _settings);
        if (cues.Contains("halfway"))
            _toneService.Play(_settings.HalfwayTone, _settings.HalfwayToneLength, _settings.HalfwayToneFile);
        var clockCues = cues.Where(c => c != "leaders" && (c != "halfway" || _settings.RaceHalfwayToneEnabled)).Select(c => c == "halfway" ? "Halfway" : c).ToArray();
        if (clockCues.Length > 0)
        {
            _lapSpeech.Stop();
            var text = string.Join(". ", clockCues);
            _lapSpeech.Enqueue(text, clockCues.Any(c => int.TryParse(c, out _)) ? Math.Max(2, _settings.RaceAnnouncementRate) : _settings.RaceAnnouncementRate,
                _settings.LapSpeechVolume, _settings.LapSpeechVoice);
            return;
        }
        if (!cues.Contains("leaders") || _lapSpeech.IsSpeakingAnnouncement) return;
        _lapSpeech.StopLaps();
        var names = standings.OrderBy(r => r.Position).Select(r =>
        {
            var entrant = SessionRacers.FirstOrDefault(e => e.Enabled && e.CarNumber == r.CarNumber);
            return Drivers.FirstOrDefault(d => d.Id == entrant?.DriverId)?.SpokenName ?? r.DriverName;
        });
        _lapSpeech.Enqueue(RaceAudioSchedule.LeaderText(names, _settings.RaceLeaderCount),
            _settings.RaceAnnouncementRate, _settings.LapSpeechVolume, _settings.LapSpeechVoice);
    }
}
