using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

public sealed class RaceEngine
{
    private sealed class RacerRuntime
    {
        public required RacerEntry Racer { get; init; }
        public List<LapRecord> Laps { get; } = [];
        public DateTimeOffset? PersonalStartedAt { get; set; }
        public bool Finished { get; set; }
        public TimeSpan? FinishElapsed { get; set; }
    }

    private sealed record AcceptedPassing(int CarNumber, DateTimeOffset Timestamp, string Source);

    private readonly Dictionary<int, RacerRuntime> _racers = [];
    private readonly List<AcceptedPassing> _acceptedPassings = [];
    private readonly List<BreakoutRecord> _breakouts = [];
    private DateTimeOffset? _armedAt;
    private DateTimeOffset? _masterStoppedAt;
    public DateTimeOffset? MasterStartedAt => _armedAt;
    private bool _forceStopped;

    public RaceState State { get; private set; } = RaceState.Ready;
    public DateTimeOffset? StartedAt { get; private set; }
    public TimeSpan Duration { get; private set; } = TimeSpan.FromMinutes(5);
    public int TargetLaps { get; private set; } = 50;
    public RaceFormat RaceFormat { get; private set; } = global::IMRCRaceControl.Models.RaceFormat.Timed;
    public TimeSpan MinimumLapTime { get; private set; } = TimeSpan.FromSeconds(1);
    public SessionType SessionType { get; private set; } = SessionType.Race;
    public StartStyle StartStyle { get; private set; } = StartStyle.Tone;
    public int LeaderCarNumber { get; private set; }
    public bool PracticeMode => SessionType == SessionType.Practice;
    public bool LapTargetReached => IsLappedRace && _racers.Values.Any(r => r.Laps.Count >= TargetLaps);
    public bool AllDriversFinished => !PracticeMode && _racers.Count > 0 && _racers.Values.All(r => r.Finished);
    public bool IsLappedRace => !PracticeMode && RaceFormat == global::IMRCRaceControl.Models.RaceFormat.Laps;

    public IReadOnlyList<LapRecord> Laps => _racers.Values
        .SelectMany(r => r.Laps)
        .OrderBy(l => l.Sequence)
        .ToArray();

    public IReadOnlyList<BreakoutRecord> Breakouts => _breakouts
        .OrderBy(b => b.Timestamp)
        .ThenBy(b => b.Sequence)
        .ToArray();

    public int EnabledRacerCount => _racers.Count;

    public void Start(
        IEnumerable<RacerEntry> racers,
        TimeSpan duration,
        int targetLaps,
        RaceFormat raceFormat,
        TimeSpan minimumLapTime,
        SessionType sessionType,
        StartStyle startStyle,
        DateTimeOffset startTime)
    {
        var enabled = racers
            .Where(r => r.Enabled)
            .OrderBy(r => r.CarNumber)
            .ToList();

        if (enabled.Count == 0)
            throw new InvalidOperationException("Enable at least one racer before starting the session.");

        if (sessionType != SessionType.Practice &&
            raceFormat == global::IMRCRaceControl.Models.RaceFormat.Timed &&
            duration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Race duration must be greater than zero.");
        }

        if (sessionType != SessionType.Practice &&
            raceFormat == global::IMRCRaceControl.Models.RaceFormat.Laps &&
            targetLaps <= 0)
        {
            throw new InvalidOperationException("Target laps must be greater than zero.");
        }

        _racers.Clear();
        foreach (var racer in enabled)
        {
            _racers[racer.CarNumber] = new RacerRuntime
            {
                Racer = CloneRacer(racer)
            };
        }

        _acceptedPassings.Clear();
        _breakouts.Clear();
        _forceStopped = false;
        Duration = duration;
        TargetLaps = Math.Max(1, targetLaps);
        RaceFormat = sessionType == SessionType.Practice ? global::IMRCRaceControl.Models.RaceFormat.Timed : raceFormat;
        MinimumLapTime = minimumLapTime < TimeSpan.Zero ? TimeSpan.Zero : minimumLapTime;
        SessionType = sessionType;
        StartStyle = sessionType == SessionType.Practice ? StartStyle.Tone : startStyle;
        LeaderCarNumber = enabled.First().CarNumber;
        _masterStoppedAt = null;
        _armedAt = startTime;
        StartedAt = null;

        if (StartStyle == StartStyle.Tone)
        {
            StartedAt = startTime;
            foreach (var runtime in _racers.Values)
                runtime.PersonalStartedAt = startTime;
            State = RaceState.Running;
        }
        else
        {
            State = RaceState.Armed;
        }
    }

    public void UpdateLiveFormat(RaceFormat format, TimeSpan duration, int targetLaps, DateTimeOffset now)
    {
        if (PracticeMode || State is not (RaceState.Armed or RaceState.Running or RaceState.TimeExpired))
            throw new InvalidOperationException("Only an active race can change its live format.");
        if (!Enum.IsDefined(format) || duration <= TimeSpan.Zero || targetLaps < 1)
            throw new ArgumentException("Choose a valid race format, duration and lap target.");
        RaceFormat = format;
        Duration = duration;
        TargetLaps = targetLaps;
        // Apply prospectively: retain every recorded lap, start clock and finished driver.
        // Unfinished drivers are evaluated against the new target on their next crossing.
        Tick(now);
    }

    public PassingResult RegisterPassing(int carNumber, string source, DateTimeOffset timestamp)
    {
        if (State is RaceState.Ready or RaceState.FalseStart or RaceState.Complete)
            return new PassingResult(false, "Session is not accepting passings.", Code: PassingResultCode.NotAccepting);

        if (!_racers.TryGetValue(carNumber, out var racer))
            return new PassingResult(false, $"Car {carNumber} is not enabled for this session.", Code: PassingResultCode.UnknownCar);

        if (racer.Finished)
            return new PassingResult(false, $"Car {carNumber} has already finished.", Code: PassingResultCode.AlreadyFinished);

        // A leader-start session is not live until the designated lead car crosses.
        if (StartStyle == StartStyle.LeaderCrossing && StartedAt is null)
        {
            if (carNumber != LeaderCarNumber)
            {
                return new PassingResult(
                    false,
                    $"PRESTART: Car {carNumber} crossed before leader Car {LeaderCarNumber}. Passing not counted.",
                    Code: PassingResultCode.PreStartRejected);
            }

            StartedAt = timestamp;
            foreach (var runtime in _racers.Values)
                runtime.PersonalStartedAt = timestamp;

            _acceptedPassings.Add(new AcceptedPassing(carNumber, timestamp, source));
            State = RaceState.Running;
            return new PassingResult(true, $"Leader Car {carNumber} crossed — all clocks started.", Code: PassingResultCode.StartCrossing);
        }

        // Individual staggered start: first crossing starts that racer's personal clock.
        if (StartStyle == StartStyle.IndividualCrossing && racer.PersonalStartedAt is null)
        {
            if (LapTargetReached)
            {
                _acceptedPassings.Add(new AcceptedPassing(carNumber, timestamp, source));
                racer.PersonalStartedAt = timestamp;
                racer.Finished = true;
                racer.FinishElapsed = TimeSpan.Zero;
                if (AllDriversFinished)
                {
                    _masterStoppedAt = timestamp;
                    State = RaceState.Complete;
                }
                return new PassingResult(true, $"Car {carNumber} — FINISHED (no timed laps).", Code: PassingResultCode.StartCrossing);
            }
            racer.PersonalStartedAt = timestamp;
            StartedAt ??= timestamp;
            _acceptedPassings.Add(new AcceptedPassing(carNumber, timestamp, source));
            State = RaceState.Running;
            return new PassingResult(true, $"Car {carNumber} clock started at {timestamp:HH:mm:ss.fff}.", Code: PassingResultCode.StartCrossing);
        }

        if (racer.PersonalStartedAt is null)
            return new PassingResult(false, $"Car {carNumber} does not have a start time yet.", Code: PassingResultCode.NotAccepting);

        var previousTimestamp = racer.Laps.LastOrDefault()?.Timestamp ?? racer.PersonalStartedAt.Value;
        var gap = timestamp - previousTimestamp;
        if (gap < MinimumLapTime)
        {
            var elapsed = timestamp - racer.PersonalStartedAt.Value;
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;

            var breakout = new BreakoutRecord(
                _breakouts.Count + 1L,
                carNumber,
                racer.Racer.DisplayName,
                racer.Laps.Count + 1,
                gap,
                elapsed,
                MinimumLapTime,
                timestamp,
                source);

            _breakouts.Add(breakout);

            return new PassingResult(
                false,
                $"Car {carNumber} BREAKOUT: {FormatLap(gap)} is below the minimum lap time of {FormatLap(MinimumLapTime)}.",
                Code: PassingResultCode.MinimumLapRejected,
                Breakout: breakout);
        }

        var previousPersonalBest = racer.Laps.Count == 0
            ? (TimeSpan?)null
            : racer.Laps.Min(l => l.LapTime);
        var previousSessionBest = _racers.Values
            .SelectMany(r => r.Laps)
            .Select(l => (TimeSpan?)l.LapTime)
            .Min();

        _acceptedPassings.Add(new AcceptedPassing(carNumber, timestamp, source));
        var lapSequence = _racers.Values.Sum(r => r.Laps.Count) + 1L;
        var lap = ApplyLap(racer, timestamp, source, lapSequence);

        var isNewPersonalBest = previousPersonalBest.HasValue && lap.LapTime < previousPersonalBest.Value;
        var isSessionBest = !previousSessionBest.HasValue || lap.LapTime < previousSessionBest.Value;
        var currentBest = racer.Laps.Min(l => l.LapTime);
        var currentBestLapNumber = racer.Laps.First(l => l.LapTime == currentBest).LapNumber;

        var racerHasFinished =
            !PracticeMode &&
            ((RaceFormat == global::IMRCRaceControl.Models.RaceFormat.Timed &&
              timestamp - racer.PersonalStartedAt.Value >= Duration) ||
             (RaceFormat == global::IMRCRaceControl.Models.RaceFormat.Laps &&
              LapTargetReached));

        if (racerHasFinished)
        {
            racer.Finished = true;
            racer.FinishElapsed = timestamp - racer.PersonalStartedAt.Value;
            lap = lap with { Finished = true };
            racer.Laps[^1] = lap;
        }

        Tick(timestamp);

        if (!PracticeMode && _racers.Values.All(r => r.Finished))
        {
            _masterStoppedAt = timestamp;
            State = RaceState.Complete;
        }

        return new PassingResult(
            true,
            $"Car {carNumber} lap {lap.LapNumber}: {FormatLap(lap.LapTime)}" + (lap.Finished ? " — FINISHED" : string.Empty),
            lap,
            PassingResultCode.LapAccepted,
            IsNewPersonalBest: isNewPersonalBest,
            IsSessionBest: isSessionBest,
            BestLap: currentBest,
            BestLapNumber: currentBestLapNumber);
    }

    public void Tick(DateTimeOffset now)
    {
        if (State is RaceState.Ready or RaceState.FalseStart or RaceState.Complete or RaceState.Armed || PracticeMode)
            return;

        if (RaceFormat == global::IMRCRaceControl.Models.RaceFormat.Laps)
        {
            State = _racers.Count > 0 && _racers.Values.All(r => r.Finished)
                ? RaceState.Complete
                : RaceState.Running;
            return;
        }

        if (StartStyle == StartStyle.IndividualCrossing)
        {
            var anyExpired = _racers.Values.Any(r =>
                !r.Finished &&
                r.PersonalStartedAt.HasValue &&
                now - r.PersonalStartedAt.Value >= Duration);

            State = anyExpired ? RaceState.TimeExpired : RaceState.Running;
            return;
        }

        if (StartedAt.HasValue && now - StartedAt.Value >= Duration)
            State = RaceState.TimeExpired;
        else
            State = RaceState.Running;
    }

    public PassingResult UndoLastPassing(DateTimeOffset now)
    {
        if (_acceptedPassings.Count == 0)
            return new PassingResult(false, "There is no passing to undo.");

        var lastAction = _acceptedPassings[^1];
        var removedLap = _racers.TryGetValue(lastAction.CarNumber, out var runtime)
            ? runtime.Laps.LastOrDefault(l => l.Timestamp == lastAction.Timestamp)
            : null;

        _acceptedPassings.RemoveAt(_acceptedPassings.Count - 1);
        Rebuild(now);

        return removedLap is null
            ? new PassingResult(true, $"Undid Car {lastAction.CarNumber} start crossing.")
            : new PassingResult(true, $"Undid Car {removedLap.CarNumber}, lap {removedLap.LapNumber}.", removedLap);
    }

    public void FalseStart()
    {
        if (State is RaceState.Ready or RaceState.Complete)
            return;

        foreach (var racer in _racers.Values)
        {
            racer.Laps.Clear();
            racer.PersonalStartedAt = null;
            racer.Finished = false;
            racer.FinishElapsed = null;
        }

        _acceptedPassings.Clear();
        _breakouts.Clear();
        StartedAt = null;
        _armedAt = null;
        _masterStoppedAt = null;
        _forceStopped = false;
        State = RaceState.FalseStart;
    }

    public void Stop(DateTimeOffset now)
    {
        if (State is RaceState.Ready or RaceState.Complete)
            return;

        _forceStopped = true;
        _masterStoppedAt = now;
        State = RaceState.Complete;
    }

    public void Reset()
    {
        _racers.Clear();
        _acceptedPassings.Clear();
        _breakouts.Clear();
        _armedAt = null;
        _masterStoppedAt = null;
        StartedAt = null;
        State = RaceState.Ready;
        _forceStopped = false;
    }

    public TimeSpan GetElapsed(DateTimeOffset now)
    {
        if (StartedAt is null)
            return TimeSpan.Zero;

        var elapsed = now - StartedAt.Value;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    public TimeSpan GetMasterElapsed(DateTimeOffset now)
    {
        if (_armedAt is null) return TimeSpan.Zero;
        var elapsed = (_masterStoppedAt ?? now) - _armedAt.Value;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    public TimeSpan GetRemaining(DateTimeOffset now)
    {
        if (PracticeMode || IsLappedRace)
            return TimeSpan.Zero;
        // The master starts at the start command, independently of personal start crossings.
        // Keep overtime signed while drivers finish their own races.
        return Duration - GetMasterElapsed(now);
    }

    public TimeSpan? GetRacerRemaining(int carNumber, DateTimeOffset now)
    {
        if (!_racers.TryGetValue(carNumber, out var racer) ||
            racer.PersonalStartedAt is null ||
            PracticeMode ||
            IsLappedRace)
        {
            return null;
        }

        var remaining = Duration - (now - racer.PersonalStartedAt.Value);
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    public IReadOnlyList<StandingRow> GetStandings(DateTimeOffset now)
    {
        var sessionBest = _racers.Values
            .SelectMany(r => r.Laps)
            .Select(l => (TimeSpan?)l.LapTime)
            .Min();

        var ordered = _racers.Values
            .Select(r => new
            {
                Runtime = r,
                Started = r.PersonalStartedAt.HasValue,
                Laps = r.Laps.Count,
                LastElapsed = r.Laps.LastOrDefault()?.Elapsed ?? TimeSpan.MaxValue
            })
            .OrderByDescending(x => x.Laps)
            .ThenByDescending(x => x.Started)
            .ThenBy(x => x.LastElapsed)
            .ThenBy(x => x.Runtime.Racer.CarNumber)
            .ToList();

        var rows = new List<StandingRow>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var x = ordered[i];
            var runtime = x.Runtime;
            var laps = runtime.Laps;
            var last = laps.LastOrDefault();
            var best = laps.Count == 0 ? (TimeSpan?)null : laps.Min(l => l.LapTime);
            var bestLapNumber = best is null ? (int?)null : laps.First(l => l.LapTime == best.Value).LapNumber;
            var isNewPersonalBest = false;
            if (last is not null && laps.Count > 1)
            {
                var previousBest = laps.Take(laps.Count - 1).Min(l => l.LapTime);
                isNewPersonalBest = last.LapTime < previousBest;
            }

            var average = Average(laps.Select(l => l.LapTime));

            var status = GetStatus(runtime, now);
            var clock = GetClock(runtime, now);
            var pace = GetPace(laps, average);
            var consistency = GetConsistency(laps);
            var gapToLeader = i == 0 ? "LEADER" : GetGap(ordered[0].Runtime, runtime);
            var gapBehind = i == ordered.Count - 1 ? "—" : GetGap(runtime, ordered[i + 1].Runtime);

            rows.Add(new StandingRow
            {
                Position = i + 1,
                CarNumber = runtime.Racer.CarNumber,
                DriverName = runtime.Racer.DisplayName,
                Laps = laps.Count,
                Clock = clock,
                LastLap = last is null ? "—" : FormatLap(last.LapTime),
                BestLap = best is null ? "—" : FormatLap(best.Value),
                BestLapNumber = bestLapNumber?.ToString() ?? "—",
                Top2 = BestConsecutive(laps, 2),
                Top3 = BestConsecutive(laps, 3),
                Top5 = BestConsecutive(laps, 5),
                AverageLap = average is null ? "—" : FormatLap(average.Value),
                Average5 = RecentAverage(laps, 5),
                Average10 = RecentAverage(laps, 10),
                Average15 = RecentAverage(laps, 15),
                Consistency = consistency,
                Pace = pace,
                GapToLeader = gapToLeader,
                GapBehind = gapBehind,
                TotalTime = last is null ? "—" : FormatElapsed(last.Elapsed),
                Status = status,
                ManualActionText = "PASS",
                IsFastestLap = best.HasValue && sessionBest.HasValue && best.Value == sessionBest.Value,
                IsNewPersonalBest = isNewPersonalBest
            });
        }

        return rows;
    }

    private string GetStatus(RacerRuntime racer, DateTimeOffset now)
    {
        if (racer.Finished)
            return "FINISHED";
        if (_forceStopped)
            return "STOPPED";
        if (racer.PersonalStartedAt is null)
            return StartStyle == StartStyle.LeaderCrossing ? "WAITING LEADER" : "WAITING START";
        if (PracticeMode)
            return "PRACTICE";
        if (RaceFormat == global::IMRCRaceControl.Models.RaceFormat.Laps)
        {
            if (LapTargetReached) return "FINAL LAP";
            var remainingLaps = Math.Max(0, TargetLaps - _racers.Values.Max(r => r.Laps.Count));
            return remainingLaps switch
            {
                0 => "FINISHED",
                1 => "FINAL LAP",
                _ => $"{remainingLaps} LAPS TO GO"
            };
        }
        if (now - racer.PersonalStartedAt.Value >= Duration)
            return "FINAL LAP";
        if (State == RaceState.Complete)
            return "COMPLETE";
        return "RACING";
    }

    private string GetClock(RacerRuntime racer, DateTimeOffset now)
    {
        if (racer.PersonalStartedAt is null)
            return "WAITING";
        if (racer.Finished && racer.FinishElapsed.HasValue)
        {
            return RaceFormat == global::IMRCRaceControl.Models.RaceFormat.Laps
                ? FormatClock(racer.FinishElapsed.Value)
                : FormatClock(TimeSpan.Zero);
        }

        var elapsed = now - racer.PersonalStartedAt.Value;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

        if (PracticeMode)
            return "+" + FormatClock(elapsed);

        if (RaceFormat == global::IMRCRaceControl.Models.RaceFormat.Laps)
            return FormatClock(elapsed);

        var remaining = Duration - elapsed;
        return FormatClock(remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining);
    }

    private string GetPace(IReadOnlyList<LapRecord> laps, TimeSpan? average)
    {
        if (PracticeMode || average is null || average.Value <= TimeSpan.Zero || laps.Count == 0)
            return "—";

        if (RaceFormat == global::IMRCRaceControl.Models.RaceFormat.Laps)
        {
            var projectedTime = TimeSpan.FromTicks(average.Value.Ticks * TargetLaps);
            return $"{TargetLaps}/{FormatElapsed(projectedTime)}";
        }

        var projectedLaps = Math.Max(laps.Count, (int)Math.Floor(Duration.TotalSeconds / average.Value.TotalSeconds));
        if (projectedLaps < 1) projectedLaps = 1;
        var projectedTimedFinish = TimeSpan.FromTicks(average.Value.Ticks * projectedLaps);
        return $"{projectedLaps}/{FormatElapsed(projectedTimedFinish)}";
    }

    private static string GetConsistency(IReadOnlyList<LapRecord> laps)
    {
        if (laps.Count < 2)
            return "—";

        var values = laps.Select(l => l.LapTime.TotalSeconds).ToArray();
        var avg = values.Average();
        if (avg <= 0)
            return "—";

        var variance = values.Average(v => Math.Pow(v - avg, 2));
        var stdDev = Math.Sqrt(variance);
        var consistency = Math.Clamp(100.0 - (stdDev / avg * 100.0), 0.0, 100.0);
        return $"{consistency:0.0}%";
    }

    private static string BestConsecutive(IReadOnlyList<LapRecord> laps, int count)
    {
        if (laps.Count < count)
            return "—";

        long bestTicks = long.MaxValue;
        var bestStartIndex = 0;
        for (var i = 0; i <= laps.Count - count; i++)
        {
            long ticks = 0;
            for (var j = 0; j < count; j++)
                ticks += laps[i + j].LapTime.Ticks;

            if (ticks < bestTicks)
            {
                bestTicks = ticks;
                bestStartIndex = i;
            }
        }

        var avg = TimeSpan.FromTicks(bestTicks / count);
        var first = laps[bestStartIndex].LapNumber;
        var last = laps[bestStartIndex + count - 1].LapNumber;
        return $"{FormatLap(avg)} L{first}-{last}";
    }

    private static string RecentAverage(IReadOnlyList<LapRecord> laps, int count)
    {
        if (laps.Count < count)
            return "—";

        var avg = Average(laps.Skip(laps.Count - count).Select(l => l.LapTime));
        return avg is null ? "—" : FormatLap(avg.Value);
    }

    private static TimeSpan? Average(IEnumerable<TimeSpan> values)
    {
        var array = values.ToArray();
        if (array.Length == 0)
            return null;
        return TimeSpan.FromTicks((long)array.Average(v => v.Ticks));
    }

    private static string GetGap(RacerRuntime ahead, RacerRuntime behind)
    {
        var lapDifference = ahead.Laps.Count - behind.Laps.Count;
        if (lapDifference > 0)
            return lapDifference == 1 ? "1 LAP" : $"{lapDifference} LAPS";

        var aheadLast = ahead.Laps.LastOrDefault();
        var behindLast = behind.Laps.LastOrDefault();
        if (aheadLast is null || behindLast is null)
            return "—";

        var gap = behindLast.Elapsed - aheadLast.Elapsed;
        if (gap < TimeSpan.Zero)
            gap = gap.Negate();
        return $"+{gap.TotalSeconds:0.000}";
    }

    private LapRecord ApplyLap(RacerRuntime racer, DateTimeOffset timestamp, string source, long sequence)
    {
        var elapsed = timestamp - racer.PersonalStartedAt!.Value;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        var priorTimestamp = racer.Laps.LastOrDefault()?.Timestamp ?? racer.PersonalStartedAt.Value;
        var lapTime = timestamp - priorTimestamp;
        if (lapTime < TimeSpan.Zero)
            lapTime = TimeSpan.Zero;

        var lap = new LapRecord(
            sequence,
            racer.Racer.CarNumber,
            racer.Racer.DisplayName,
            racer.Laps.Count + 1,
            lapTime,
            elapsed,
            timestamp,
            source,
            false);

        racer.Laps.Add(lap);
        return lap;
    }

    private void Rebuild(DateTimeOffset now)
    {
        var history = _acceptedPassings.ToArray();

        foreach (var racer in _racers.Values)
        {
            racer.Laps.Clear();
            racer.PersonalStartedAt = null;
            racer.Finished = false;
            racer.FinishElapsed = null;
        }

        StartedAt = null;
        _masterStoppedAt = null;
        _forceStopped = false;

        if (StartStyle == StartStyle.Tone)
        {
            StartedAt = _armedAt;
            foreach (var racer in _racers.Values)
                racer.PersonalStartedAt = _armedAt;
            State = RaceState.Running;
        }
        else
        {
            State = RaceState.Armed;
        }

        _acceptedPassings.Clear();
        foreach (var action in history)
            RegisterPassing(action.CarNumber, action.Source, action.Timestamp);

        Tick(now);
        if (!PracticeMode && _racers.Count > 0 && _racers.Values.All(r => r.Finished))
            State = RaceState.Complete;
    }

    private static RacerEntry CloneRacer(RacerEntry racer) => new()
    {
        CarNumber = racer.CarNumber,
        Enabled = racer.Enabled,
        DriverId = racer.DriverId,
        DriverName = string.IsNullOrWhiteSpace(racer.DriverName) ? $"Car {racer.CarNumber}" : racer.DriverName.Trim(),
        Nickname = racer.Nickname?.Trim() ?? string.Empty,
        ClassId = racer.ClassId,
        ClassName = racer.ClassName,
        TransponderId = racer.TransponderId?.Trim() ?? string.Empty
    };

    public static string FormatLap(TimeSpan value) =>
        value.TotalMinutes >= 1
            ? $"{(int)value.TotalMinutes}:{value.Seconds:00}.{value.Milliseconds:000}"
            : $"{value.TotalSeconds:0.000}";

    public static string FormatElapsed(TimeSpan value) =>
        $"{(int)value.TotalMinutes}:{value.Seconds:00}.{value.Milliseconds:000}";

    public static string FormatMasterClock(TimeSpan value) =>
        value < TimeSpan.Zero ? "-" + FormatClock(value.Duration()) : FormatClock(value);

    public static string FormatClock(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return $"{(int)value.TotalMinutes}:{value.Seconds:00}.{value.Milliseconds / 100}";
    }
}
