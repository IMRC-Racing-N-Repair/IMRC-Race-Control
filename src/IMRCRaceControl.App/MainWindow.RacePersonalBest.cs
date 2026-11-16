using System.Globalization;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    // Capture the baseline before this heat starts. A newly completed race
    // must never compare its result against itself.
    private readonly Dictionary<int, DriverPersonalBest?> _racePBAtStart = new();

    private void CaptureRacePersonalBestBaselines()
    {
        _racePBAtStart.Clear();
        if (_raceEngine.PracticeMode) return;

        var format = _raceEngine.RaceFormat;
        var distance = format == RaceFormat.Laps
            ? _raceEngine.TargetLaps
            : (int)Math.Round(_raceEngine.Duration.TotalSeconds, MidpointRounding.AwayFromZero);
        if (distance <= 0) return;
        foreach (var racer in SessionRacers)
        {
            if (string.IsNullOrWhiteSpace(racer.DriverId) ||
                string.IsNullOrWhiteSpace(racer.ClassId)) continue;
            var previous = PersonalBests.Lookup(_settings.DriverPersonalBests,
                racer.DriverId, racer.ClassId, format, distance);
            _racePBAtStart[racer.CarNumber] = previous;
        }
    }

    private static bool TryPBElapsed(string? displayed, out double seconds)
    {
        seconds = 0;
        var parts = (displayed ?? "").Split(':',2);
        return parts.Length == 2 &&
            int.TryParse(parts[0],NumberStyles.None,CultureInfo.InvariantCulture,out var minutes) &&
            double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var remainder) &&
            minutes >= 0 && remainder is >= 0 and < 60 &&
            (seconds = minutes*60.0+remainder) > 0;
    }

    private void UpdateRacePersonalBestRows()
    {
        if (_recallMode || _raceEngine.PracticeMode ||
            _raceEngine.State is RaceState.Ready or RaceState.FalseStart)
        {
            foreach(var row in Standings) { row.RacePersonalBestText=""; row.IsNewRacePersonalBest=false; }
            return;
        }
        var format = _raceEngine.RaceFormat;
        var distance = format == RaceFormat.Laps ? _raceEngine.TargetLaps
            : (int)Math.Round(_raceEngine.Duration.TotalSeconds,MidpointRounding.AwayFromZero);
        if (distance <= 0) return;

        foreach (var row in Standings)
        {
            var text = "";
            var newRecord = false;
            if (_racePBAtStart.TryGetValue(row.CarNumber,out var baseline) &&
                TryPBElapsed(row.TotalTime,out var elapsed))
            {
                var state = RacePersonalBestDisplay.Evaluate(baseline,
                    format,distance,row.Laps,elapsed,row.IsFinished);
                text = state.Text;
                newRecord = state.IsNewRecord;
            }
            // One property assignment per tick prevents 100-ms text blinking
            // and unnecessary WPF layout recalculations during live scoring.
            row.RacePersonalBestText = text;
            row.IsNewRacePersonalBest = newRecord;
        }
    }

    private void IndexCompletedRacePersonalBests(EventSessionDefinition session)
    {
        if (_activeEvent is null || session.SessionType is not
            (SessionType.Qualifying or SessionType.Race)) return;
        var format = session.RaceFormat;
        var distance = format == RaceFormat.Laps ? session.TargetLaps
            : (int)Math.Round(_raceEngine.Duration.TotalSeconds,MidpointRounding.AwayFromZero);
        if (distance <= 0) return;
        foreach (var row in session.Results)
        {
            if (!row.IsFinished || !TryPBElapsed(row.TotalTime,out var seconds)) continue;
            var racer = SessionRacers.FirstOrDefault(r => r.CarNumber == row.CarNumber);
            if (racer is null || string.IsNullOrWhiteSpace(racer.DriverId) ||
                string.IsNullOrWhiteSpace(racer.ClassId)) continue;
            var prior = PersonalBests.Lookup(_settings.DriverPersonalBests,
                racer.DriverId,racer.ClassId,format,distance);
            if (!PersonalBests.Beats(prior,row.Laps,seconds,format,distance)) continue;
            _settings.DriverPersonalBests.RemoveAll(p =>
                p.DriverId == racer.DriverId && p.ClassId == racer.ClassId &&
                p.Format == format && p.Distance == distance);
            _settings.DriverPersonalBests.Add(new DriverPersonalBest(
                racer.DriverId,racer.ClassId,format,distance,row.Laps,seconds,
                session.CompletedAt ?? DateTimeOffset.Now,_activeEvent.Track?.Id ?? "",
                _activeEvent.Id,session.Id,"Completed race result"));
        }
    }
}
