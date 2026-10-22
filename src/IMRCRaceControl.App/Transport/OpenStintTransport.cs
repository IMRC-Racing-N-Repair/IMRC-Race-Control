using System.Diagnostics;
using IMRCRaceControl.Models;
using NetMQ;
using NetMQ.Sockets;

namespace IMRCRaceControl.Transport;

/// <summary>Owns the ZeroMQ socket on one worker thread. No SDR/USB access is required here.</summary>
public sealed class OpenStintTransport(string host, int port) : IByteTransport
{
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private volatile bool _connected;
    public string DisplayName => $"OpenStint {host}:{port}";
    public bool IsConnected => _connected;
    public event EventHandler<BytesReceivedEventArgs>? BytesReceived;
    public event EventHandler<TransportStatusEventArgs>? StatusChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_worker is { IsCompleted: false }) return;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = _cts.Token;
        StatusChanged?.Invoke(this, new(TransportState.Connecting, $"Waiting for OpenStint at {host}:{port}..."));
        _worker = Task.Factory.StartNew(() => Receive(token, ready), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await ready.Task.ConfigureAwait(false);
    }

    private void Receive(CancellationToken token, TaskCompletionSource ready)
    {
        try
        {
            using var socket = new SubscriberSocket();
            socket.Options.Linger = TimeSpan.Zero;
            socket.Options.ReceiveHighWatermark = 4096;
            socket.Options.MaxMsgSize = 4096;
            socket.Subscribe("");
            socket.Connect($"tcp://{host}:{port}");
            var lastMessage = Stopwatch.StartNew();
            while (!token.IsCancellationRequested)
            {
                if (socket.TryReceiveFrameBytes(TimeSpan.FromMilliseconds(100), out var frame, out var more))
                {
                    // OpenStint sends one text frame. Never turn unexpected multipart payloads into laps.
                    if (more)
                    {
                        while (more && !token.IsCancellationRequested)
                            socket.TryReceiveFrameBytes(TimeSpan.FromMilliseconds(100), out _, out more);
                        continue;
                    }
                    if (frame.Length < 2 || frame[1] != (byte)' ' || frame[0] is not ((byte)'P' or (byte)'S' or (byte)'T' or (byte)'L')) continue;
                    if (!_connected)
                    {
                        _connected = true;
                        StatusChanged?.Invoke(this, new(TransportState.Connected, $"Receiving OpenStint at {host}:{port}."));
                        ready.TrySetResult();
                    }
                    lastMessage.Restart();
                    BytesReceived?.Invoke(this, new(frame));
                }
                if (lastMessage.Elapsed > TimeSpan.FromSeconds(_connected ? 15 : 6))
                    throw new TimeoutException("No OpenStint data received. Start the decoder and check its host/port (default 5556).");
            }
            ready.TrySetCanceled(token);
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) ready.TrySetCanceled(token);
            else
            {
                StatusChanged?.Invoke(this, new(TransportState.Error, $"OpenStint connection lost: {ex.Message}", ex));
                ready.TrySetException(ex);
            }
        }
        finally { _connected = false; }
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        if (_worker is not null) await _worker.ConfigureAwait(false);
        _worker = null; _cts?.Dispose(); _cts = null; _connected = false;
        StatusChanged?.Invoke(this, new(TransportState.Disconnected, "OpenStint disconnected."));
    }
    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
