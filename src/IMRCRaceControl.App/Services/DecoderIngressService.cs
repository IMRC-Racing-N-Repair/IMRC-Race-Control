using System.Threading.Channels;
using IMRCRaceControl.Core;

namespace IMRCRaceControl.Services;

/// <summary>
/// Fast decoder ingress stage. Raw transport callbacks only enqueue packets;
/// formatting/diagnostic work happens on a background worker so decoder I/O is
/// never blocked by the WPF UI. The channel is single-reader to preserve exact
/// byte-chunk order for the AMB/MyLaps P3 stream parser.
/// </summary>
public sealed class DecoderIngressService : IAsyncDisposable
{
    private readonly Channel<Packet> _queue = Channel.CreateUnbounded<Packet>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    private readonly Task _worker;
    private long _sequence;
    private readonly AutomaticDecoder _decoder = new();
    private readonly AutomaticDecoder _trackmate = new(trackmateMode: true);
    private readonly OpenStintDecoder _openStint = new();
    private string? _transport;
    private bool _disposed;

    public DecoderIngressService()
    {
        _worker = Task.Run(ProcessAsync);
    }

    public int LogicalProcessorCount => Environment.ProcessorCount;

    public event Action<FormattedPacket>? PacketFormatted;

    public void Enqueue(string transport, byte[] data, DateTimeOffset timestamp)
    {
        if (_disposed)
            return;

        var sequence = Interlocked.Increment(ref _sequence);
        _queue.Writer.TryWrite(new Packet(sequence, transport, data, timestamp));
    }

    public void ResetConnection() => _queue.Writer.TryWrite(new Packet(0, "", [], default));

    private async Task ProcessAsync()
    {
        await foreach (var packet in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (packet.Sequence == 0)
            {
                _decoder.ResetConnection();
                _trackmate.ResetConnection();
                _openStint.ResetConnection();
                continue;
            }
            if (_transport != packet.Transport)
            {
                _decoder.ResetConnection();
                _trackmate.ResetConnection();
                _openStint.ResetConnection(clearHistory: true);
                _transport = packet.Transport;
            }
            var decoded = packet.Transport.StartsWith("OpenStint ", StringComparison.Ordinal)
                ? _openStint.FeedMessage(packet.Data, packet.Timestamp)
                : packet.Transport.StartsWith("Trackmate ", StringComparison.Ordinal)
                    ? _trackmate.Feed(packet.Data, packet.Timestamp) : _decoder.Feed(packet.Data, packet.Timestamp);
            var formatted = new FormattedPacket(
                packet.Sequence,
                packet.Transport,
                packet.Timestamp,
                packet.Data.Length,
                Convert.ToHexString(packet.Data),
                TrafficLogService.ToSafeAscii(packet.Data),
                decoded.Passings, decoded.Summary, decoded.Protocol);

            PacketFormatted?.Invoke(formatted);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _queue.Writer.TryComplete();
        try { await _worker.ConfigureAwait(false); } catch { }
    }

    private sealed record Packet(long Sequence, string Transport, byte[] Data, DateTimeOffset Timestamp);

    public sealed record FormattedPacket(
        long Sequence,
        string Transport,
        DateTimeOffset Timestamp,
        int Bytes,
        string Hex,
        string Ascii,
        IReadOnlyList<DecoderPassing> Passings,
        string DecodeSummary,
        string Protocol);
}
