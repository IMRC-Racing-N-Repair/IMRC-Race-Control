using IMRCRaceControl.Models;

namespace IMRCRaceControl.Transport;

public sealed class BytesReceivedEventArgs(byte[] data) : EventArgs
{
    public byte[] Data { get; } = data;
}

public sealed class TransportStatusEventArgs(
    TransportState state,
    string message,
    Exception? exception = null) : EventArgs
{
    public TransportState State { get; } = state;
    public string Message { get; } = message;
    public Exception? Exception { get; } = exception;
}
