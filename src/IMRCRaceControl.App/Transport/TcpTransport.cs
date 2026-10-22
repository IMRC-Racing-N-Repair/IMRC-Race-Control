using System.IO;
using System.Net.Sockets;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Transport;

public sealed class TcpTransport(string host, int port) : IByteTransport
{
    private readonly string _host = host;
    private readonly int _port = port;
    private TcpClient? _client;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private int _disconnecting;

    public string DisplayName => $"LAN {_host}:{_port}";
    public bool IsConnected => _client?.Connected == true;

    public event EventHandler<BytesReceivedEventArgs>? BytesReceived;
    public event EventHandler<TransportStatusEventArgs>? StatusChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
            return;

        StatusChanged?.Invoke(this, new(TransportState.Connecting, $"Connecting to {_host}:{_port}..."));

        _client = new TcpClient
        {
            NoDelay = true,
            ReceiveBufferSize = 65536
        };

        _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(6));

        try
        {
            await _client.ConnectAsync(_host, _port, timeoutCts.Token);
            _readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _readTask = ReadLoopAsync(_readCts.Token);

            StatusChanged?.Invoke(this, new(TransportState.Connected, $"Connected to {_host}:{_port}."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            StatusChanged?.Invoke(this, new(TransportState.Error, $"LAN connection failed: {ex.Message}", ex));
            await DisconnectAsync();
            throw;
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        try
        {
            var stream = _client!.GetStream();

            while (!cancellationToken.IsCancellationRequested)
            {
                var count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                    throw new IOException("The decoder closed the TCP connection.");

                var data = new byte[count];
                Buffer.BlockCopy(buffer, 0, data, 0, count);
                BytesReceived?.Invoke(this, new(data));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (Volatile.Read(ref _disconnecting) == 0)
                StatusChanged?.Invoke(this, new(TransportState.Error, $"LAN connection lost: {ex.Message}", ex));
        }
    }

    public async Task DisconnectAsync()
    {
        if (Interlocked.Exchange(ref _disconnecting, 1) == 1)
            return;

        try
        {
            _readCts?.Cancel();

            try
            {
                _client?.Client.Shutdown(SocketShutdown.Both);
            }
            catch
            {
            }

            _client?.Close();

            if (_readTask is not null)
            {
                try
                {
                    await _readTask.WaitAsync(TimeSpan.FromSeconds(1));
                }
                catch
                {
                }
            }
        }
        finally
        {
            _readCts?.Dispose();
            _readCts = null;
            _readTask = null;
            _client?.Dispose();
            _client = null;
            Interlocked.Exchange(ref _disconnecting, 0);
            StatusChanged?.Invoke(this, new(TransportState.Disconnected, "LAN disconnected."));
        }
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
