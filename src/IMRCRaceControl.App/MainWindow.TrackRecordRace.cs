using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private TrackRecordRace? _trackRecordRace;
    private string _recordFallbackClassId = "";
    private string _recordFallbackClassName = "";

    private void BeginTrackRecordRace(ClassDefinition cls, SessionType type)
    {
        _trackRecordRace = null;
        if (_activeEvent?.Track is not { } track || type is not (SessionType.Qualifying or SessionType.Race)) return;
        var excluded = (_loadedSession?.CombinedMembers ?? []).Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_loadedSession is not null) excluded.Add(_loadedSession.Id);
        _recordFallbackClassId = cls.Id;
        _recordFallbackClassName = cls.Name;
        _trackRecordRace = new TrackRecordRace(TrackRecords.BestTimes(track.Id, Events, excluded));
    }

    private RaceTrackRecord? TrackRecordOnFinish(LapRecord? finish)
    {
        if (_trackRecordRace is null || finish is not { Finished: true }) return null;
        var driver = SessionRacers.FirstOrDefault(r => r.CarNumber == finish.CarNumber);
        if (driver is null) return null;
        var classId = string.IsNullOrWhiteSpace(driver.ClassId) ? _recordFallbackClassId : driver.ClassId;
        var className = string.IsNullOrWhiteSpace(driver.ClassName) ? _recordFallbackClassName : driver.ClassName;
        return _trackRecordRace.DriverFinished(finish, classId, className, _raceEngine.Laps);
    }

    private void RecheckTrackRecords()
    {
        if (_trackRecordRace is null) return;
        _trackRecordRace.Reset();
        foreach (var finish in _raceEngine.Laps.Where(l => l.Finished))
            TrackRecordOnFinish(finish);
    }

    private string LiveTrackRecordNotice => !_recallMode && !_practiceActive &&
        _raceEngine.State is RaceState.Running or RaceState.TimeExpired or RaceState.Complete
        ? _trackRecordRace?.Latest?.Banner ?? "" : "";

    internal string SpectatorTrackRecordNotice => _finishedSpectatorFrame?.TrackRecord ?? LiveTrackRecordNotice;
}
