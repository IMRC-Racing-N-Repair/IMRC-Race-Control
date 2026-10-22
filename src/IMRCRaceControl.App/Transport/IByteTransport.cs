namespace IMRCRaceControl.Transport;

public interface IByteTransport : IAsyncDisposable
{
    string DisplayName { get; }
    bool IsConnected { get; }

    event EventHandler<BytesReceivedEventArgs>? BytesReceived;
    event EventHandler<TransportStatusEventArgs>? StatusChanged;

    Task ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync();
}
