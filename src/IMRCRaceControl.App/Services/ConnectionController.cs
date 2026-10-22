using IMRCRaceControl.Models;
using IMRCRaceControl.Transport;

namespace IMRCRaceControl.Services;

public sealed class ConnectionController : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _sessionCts;
    private IByteTransport? _transport;
    private Func<IByteTransport>? _factory;
    private bool _autoReconnect;
    private bool _stopping;
    private int _reconnectLoopRunning;

    public IByteTransport? CurrentTransport => _transport;

    public event EventHandler<BytesReceivedEventArgs>? BytesReceived;
    public event EventHandler<TransportStatusEventArgs>? StatusChanged;

    public async Task StartAsync(Func<IByteTransport> factory, bool autoReconnect)
    {
        await StopAsync();

        DecoderDiagnostics.Write("START", $"Connection requested; autoReconnect={autoReconnect}");
        _factory = factory;
        _autoReconnect = autoReconnect;
        _stopping = false;
        _sessionCts = new CancellationTokenSource();

        try
        {
            await ConnectNewTransportAsync(_sessionCts.Token);
        }
        catch when (_autoReconnect && !_stopping)
        {
            // The transport already raised an Error status, which starts the reconnect loop.
            // Keep the session alive so the decoder can be unplugged/replugged or powered later.
        }
    }

    private async Task ConnectNewTransportAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_stopping || _factory is null)
                return;

            if (_transport is not null)
            {
                _transport.BytesReceived -= OnBytesReceived;
                _transport.StatusChanged -= OnStatusChanged;
                await _transport.DisposeAsync();
            }

            _transport = _factory();
            _transport.BytesReceived += OnBytesReceived;
            _transport.StatusChanged += OnStatusChanged;

            await _transport.ConnectAsync(token);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnBytesReceived(object? sender, BytesReceivedEventArgs e) =>
        BytesReceived?.Invoke(sender ?? this, e);

    private void OnStatusChanged(object? sender, TransportStatusEventArgs e)
    {
        DecoderDiagnostics.Write(e.State.ToString(), $"{(sender as IByteTransport)?.DisplayName ?? "Connection"}: {e.Message}", e.Exception);
        StatusChanged?.Invoke(sender ?? this, e);

        if (e.State == TransportState.Error && _autoReconnect && !_stopping)
            _ = ReconnectLoopAsync();
    }

    private async Task ReconnectLoopAsync()
    {
        if (Interlocked.Exchange(ref _reconnectLoopRunning, 1) == 1)
            return;

        try
        {
            var delay = TimeSpan.FromSeconds(2);

            while (!_stopping && _autoReconnect && _sessionCts is { IsCancellationRequested: false })
            {
                DecoderDiagnostics.Write("RETRY", $"Reconnecting in {delay.TotalSeconds:0} seconds");
                StatusChanged?.Invoke(this, new(
                    TransportState.Connecting,
                    $"Reconnecting in {delay.TotalSeconds:0} seconds..."));

                try
                {
                    await Task.Delay(delay, _sessionCts.Token);
                    await ConnectNewTransportAsync(_sessionCts.Token);

                    if (_transport?.IsConnected == true)
                        return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    DecoderDiagnostics.Write("RETRY FAILED", "Reconnect attempt failed", ex);
                    var nextSeconds = Math.Min(delay.TotalSeconds * 2, 15);
                    delay = TimeSpan.FromSeconds(nextSeconds);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _reconnectLoopRunning, 0);
        }
    }

    public async Task StopAsync()
    {
        DecoderDiagnostics.Write("STOP", "Connection stop requested");
        _stopping = true;
        _sessionCts?.Cancel();

        await _gate.WaitAsync();
        try
        {
            if (_transport is not null)
            {
                _transport.BytesReceived -= OnBytesReceived;
                _transport.StatusChanged -= OnStatusChanged;
                await _transport.DisposeAsync();
                _transport = null;
            }

            _sessionCts?.Dispose();
            _sessionCts = null;
        }
        finally
        {
            _gate.Release();
        }

        StatusChanged?.Invoke(this, new(TransportState.Disconnected, "Disconnected."));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _gate.Dispose();
    }
}
