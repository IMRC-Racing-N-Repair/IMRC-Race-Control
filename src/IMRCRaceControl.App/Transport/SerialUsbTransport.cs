using System.IO.Ports;
using IMRCRaceControl.Services;
using IMRCRaceControl.Models;
using IMRCRaceControl.Core;

namespace IMRCRaceControl.Transport;

public sealed class SerialUsbTransport(
    string portName,
    int baudRate,
    int dataBits,
    Parity parity,
    StopBits stopBits,
    Handshake handshake,
    bool initializeILap = false,
    bool initializeTrackmate = false) : IByteTransport
{
    private readonly string _portName = portName;
    private readonly int _baudRate = baudRate;
    private readonly int _dataBits = dataBits;
    private readonly Parity _parity = parity;
    private readonly StopBits _stopBits = stopBits;
    private readonly Handshake _handshake = handshake;

    private SerialPort? _port;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private int _disconnecting;

    public string DisplayName => initializeTrackmate ? $"Trackmate {_portName}" : $"USB/Serial {_portName}";
    public bool IsConnected => _port?.IsOpen == true;

    public event EventHandler<BytesReceivedEventArgs>? BytesReceived;
    public event EventHandler<TransportStatusEventArgs>? StatusChanged;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
            return Task.CompletedTask;

        StatusChanged?.Invoke(this, new(TransportState.Connecting, $"Opening {_portName}..."));

        var stage = "opening serial port";
        DecoderDiagnostics.Write("SERIAL SETTINGS", $"{DisplayName}; baud={_baudRate}; dataBits={_dataBits}; parity={_parity}; stopBits={_stopBits}; handshake={_handshake}; read timeout=infinite; write timeout=5000ms; Trackmate={initializeTrackmate}; iLap={initializeILap}");
        try
        {
            _port = new SerialPort(_portName, _baudRate, _parity, _dataBits, _stopBits)
            {
                Handshake = _handshake,
                ReadBufferSize = 65536,
                ReadTimeout = SerialPort.InfiniteTimeout,
                WriteTimeout = 5000
            };

            _port.Open();
            // Explicit hardware profiles alone send startup commands, once per connection.
            if (initializeTrackmate)
            {
                stage = "sending Trackmate start command";
                var start = TrackmateProtocol.StartCommand;
                DecoderDiagnostics.Write("TX", $"{DisplayName}; start command={Convert.ToHexString(start)}");
                _port.DiscardInBuffer();
                _port.Write(start, 0, start.Length);
            }
            else if (initializeILap)
            {
                stage = "sending iLap start command";
                _port.Write(new byte[] { 0x01, 0x25, 0x0d, 0x0a }, 0, 4);
            }

            _readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _readTask = ReadLoopAsync(_readCts.Token);

            StatusChanged?.Invoke(this, new(
                TransportState.Connected,
                $"Connected to {_portName} @ {_baudRate} baud."));
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, new(TransportState.Error, $"USB/Serial failed while {stage}: {ex.Message}", ex));
            _port?.Dispose();
            _port = null;
            throw;
        }

        return Task.CompletedTask;
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        try
        {
            var stream = _port!.BaseStream;
            var idleTimeoutLogged = false;

            while (!cancellationToken.IsCancellationRequested)
            {
                int count;
                try
                {
                    count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException) when (!cancellationToken.IsCancellationRequested && _port?.IsOpen == true)
                {
                    // Some serial drivers report a read timeout despite InfiniteTimeout.
                    // Silence between crossings is normal; keep the same connection alive.
                    if (!idleTimeoutLogged)
                    {
                        DecoderDiagnostics.Write("IDLE", $"{DisplayName}: serial read timed out; continuing to wait for crossings");
                        idleTimeoutLogged = true;
                    }
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                idleTimeoutLogged = false;
                if (count <= 0)
                    continue;

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
                StatusChanged?.Invoke(this, new(TransportState.Error, $"USB/Serial connection lost: {ex.Message}", ex));
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
                _port?.Close();
            }
            catch
            {
            }

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
            _port?.Dispose();
            _port = null;
            Interlocked.Exchange(ref _disconnecting, 0);
            StatusChanged?.Invoke(this, new(TransportState.Disconnected, "USB/Serial disconnected."));
        }
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
