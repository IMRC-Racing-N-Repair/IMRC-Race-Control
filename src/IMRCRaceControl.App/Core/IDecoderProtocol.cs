namespace IMRCRaceControl.Core;

/// <summary>
/// Decoder-specific protocol parsers live behind this boundary.
/// The scoring engine will consume normalized PassingEvent records only.
/// </summary>
public interface IDecoderProtocol
{
    string Name { get; }
    event Action<PassingEvent>? PassingReceived;

    void Feed(ReadOnlyMemory<byte> data);
    void Reset();
}
