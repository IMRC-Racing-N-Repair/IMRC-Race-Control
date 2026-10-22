using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Transport;

public sealed class SimulatorTransport : IByteTransport
{
    private CancellationTokenSource? _cts;
    private Task? _task;
    private int _counter;

    public string DisplayName => "Built-in Simulator";
    public bool IsConnected => _task is { IsCompleted: false };

    public event EventHandler<BytesReceivedEventArgs>? BytesReceived;
    public event EventHandler<TransportStatusEventArgs>? StatusChanged;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _task = RunAsync(_cts.Token);
        StatusChanged?.Invoke(this, new(TransportState.Connected, "Simulator connected."));
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken token)
    {
        var ids = new uint[] { 3358118, 4728192, 8821045, 1010101, 7654321 };

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(700, token);
                var id = ids[_counter % ids.Length];
                var frame = SimulatorPassing.Encode(id, (uint)_counter, DateTimeOffset.UtcNow,
                    18 + (_counter % 25), 70 + (_counter % 25));
                _counter++;
                BytesReceived?.Invoke(this, new(frame));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();

        if (_task is not null)
        {
            try { await _task; } catch { }
        }

        _cts?.Dispose();
        _cts = null;
        _task = null;
        StatusChanged?.Invoke(this, new(TransportState.Disconnected, "Simulator disconnected."));
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
