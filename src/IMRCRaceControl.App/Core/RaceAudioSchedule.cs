using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

/// <summary>Race-clock cues; no wall-clock timers or queued catch-up announcements.</summary>
public sealed class RaceAudioSchedule
{
    private readonly HashSet<int> _crossed = [];
    private readonly HashSet<string> _fired = [];
    private double _previousElapsed = -1;
    private int _lastLeaderSlot;
    public void Reset() { _crossed.Clear(); _fired.Clear(); _previousElapsed = -1; _lastLeaderSlot = 0; }
    public bool FirstCrossing(int car) => _crossed.Add(car);
    public IReadOnlyList<string> Advance(double elapsed, double duration, bool lapped, int leadingLaps, int targetLaps, AppSettings settings)
    {
        var cues = new List<string>();
        elapsed = Math.Max(0, elapsed);
        bool Once(string id, bool due) => due && _fired.Add(id);
        var remaining = duration - elapsed;
        if (Once("halfway", lapped ? leadingLaps >= (int)Math.Ceiling(targetLaps / 2.0) : duration > 0 && elapsed >= duration / 2))
            if (settings.RaceHalfwayToneEnabled || settings.HalfwayTone != TonePattern.Silent) cues.Add("halfway");
        if (!lapped && duration > 0)
        {
            foreach (var seconds in new[] { 120, 60 })
                if (Once("minute" + seconds, duration >= seconds && _previousElapsed < duration - seconds && elapsed >= duration - seconds))
                    if (remaining > (seconds == 120 ? 60 : 0) && (seconds == 120 ? settings.RaceTwoMinutesEnabled : settings.RaceOneMinuteEnabled))
                        cues.Add(seconds == 120 ? "2 minutes to go" : "1 minute to go");
            int count = (int)Math.Ceiling(remaining);
            if (settings.RaceCountdownEnabled && count >= 1 && count <= Math.Clamp(settings.RaceCountdownSeconds, 1, 30)
                && Once("count" + count, elapsed > 0)) cues.Add(count.ToString());
        }
        int interval = Math.Clamp(settings.RaceLeaderIntervalSeconds, 0, 60);
        int slot = interval == 0 ? 0 : (int)(elapsed / interval);
        if (slot > _lastLeaderSlot)
        {
            _lastLeaderSlot = slot;
            // Do not collide with the countdown or announce leaders after the master clock ends.
            if (settings.RaceLeadersEnabled && cues.Count == 0 && (lapped || remaining > (settings.RaceCountdownEnabled ? settings.RaceCountdownSeconds : 0))) cues.Add("leaders");
        }
        _previousElapsed = elapsed;
        return cues;
    }
    public static string LeaderText(IEnumerable<string> names, int count)
    {
        var selected = names.Where(n => !string.IsNullOrWhiteSpace(n)).Take(Math.Clamp(count, 1, 5)).ToArray();
        return selected.Length switch
        {
            0 => "",
            1 => "Current leader is " + selected[0],
            2 => "Current leaders are " + selected[0] + " and " + selected[1],
            _ => "Current leaders are " + string.Join(", ", selected[..^1]) + ", and " + selected[^1]
        };
    }
}
