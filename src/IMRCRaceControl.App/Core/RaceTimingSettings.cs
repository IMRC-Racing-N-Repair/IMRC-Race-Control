using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

/// <summary>Actual engine settings, or the saved/session settings used to load a race.</summary>
public readonly record struct RaceTimingSettings(RaceFormat Format, TimeSpan Duration, int TargetLaps)
{
    public static RaceTimingSettings ForSession(EventSessionDefinition session, ClassDefinition? cls)
    {
        // Completed sessions save the engine duration in RaceMinutesOverride.
        // Scheduled sessions load the override first, then the class's qualifying/main minutes.
        // No arbitrary five-minute fallback when both are missing.
        var minutes = session.RaceMinutesOverride ?? cls?.MinutesFor(session.SessionType);
        var duration = TimeSpan.Zero;
        if (session.RaceFormat == RaceFormat.Timed && minutes is double value && double.IsFinite(value) && value > 0)
        {
            try { duration = TimeSpan.FromMinutes(value); }
            catch (OverflowException) { /* Invalid historical duration; leave it unknown. */ }
        }
        // StartRaceButton_Click uses session.TargetLaps when positive, otherwise cls.TargetLaps.
        // The main-specific target is already copied into newly generated main sessions.
        var targetLaps = session.TargetLaps > 0 ? session.TargetLaps : cls?.TargetLaps ?? 0;
        return new(session.RaceFormat, duration, targetLaps);
    }

    public string? FormatName => Format switch
    {
        RaceFormat.Timed => "timed",
        RaceFormat.Laps => "laps",
        _ => null
    };

    public double? DurationSeconds => Format == RaceFormat.Timed && Duration > TimeSpan.Zero ? Duration.TotalSeconds : null;
    public int? LapTarget => Format == RaceFormat.Laps && TargetLaps > 0 ? TargetLaps : null;
}
