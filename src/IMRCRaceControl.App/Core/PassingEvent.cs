namespace IMRCRaceControl.Core;

public sealed record PassingEvent(
    string TransponderId,
    DateTimeOffset Timestamp,
    string DecoderId,
    string LoopId,
    int? HitCount,
    int? SignalStrength,
    ReadOnlyMemory<byte> RawData);
