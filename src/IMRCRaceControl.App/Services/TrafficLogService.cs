using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace IMRCRaceControl.Services;

public sealed class TrafficLogService : IAsyncDisposable
{
    private readonly object _startLock = new();
    private readonly Channel<LogEntry> _queue = Channel.CreateUnbounded<LogEntry>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    private FileStream? _rawStream;
    private StreamWriter? _jsonWriter;
    private Task? _writerTask;
    private bool _disposed;

    public string SessionDirectory { get; private set; } = string.Empty;
    public string RawFilePath { get; private set; } = string.Empty;
    public string JsonLogPath { get; private set; } = string.Empty;

    public event Action<Exception>? LoggingError;

    public void AppendReceived(string transport, byte[] data, DateTimeOffset timestamp)
    {
        if (_disposed)
            return;

        try { EnsureStarted(); }
        catch (Exception ex)
        {
            DecoderDiagnostics.Write("TRAFFIC LOG ERROR", "Cannot open raw traffic log; scoring continues", ex);
            LoggingError?.Invoke(ex);
            return;
        }

        if (!_queue.Writer.TryWrite(new LogEntry(transport, data, timestamp)))
            LoggingError?.Invoke(new IOException("Decoder log queue rejected incoming data."));
    }

    public Task AppendReceivedAsync(string transport, byte[] data, DateTimeOffset timestamp)
    {
        AppendReceived(transport, data, timestamp);
        return Task.CompletedTask;
    }

    private void EnsureStarted()
    {
        if (_writerTask is not null)
            return;

        lock (_startLock)
        {
            if (_writerTask is not null)
                return;

            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IMRCRaceControl",
                "Logs");

            Directory.CreateDirectory(baseDir);

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fffffff");
            SessionDirectory = baseDir;
            RawFilePath = Path.Combine(baseDir, $"decoder_{stamp}.bin");
            JsonLogPath = Path.Combine(baseDir, $"decoder_{stamp}.jsonl");

            _rawStream = new FileStream(
                RawFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 65536,
                useAsync: true);

            _jsonWriter = new StreamWriter(
                new FileStream(
                    JsonLogPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 65536,
                    useAsync: true),
                new UTF8Encoding(false));

            _writerTask = WriterLoopAsync();
        }
    }

    private async Task WriterLoopAsync()
    {
        try
        {
            await foreach (var entry in _queue.Reader.ReadAllAsync())
            {
                await _rawStream!.WriteAsync(entry.Data);

                var record = new
                {
                    timestampUtc = entry.Timestamp.UtcDateTime,
                    direction = "RX",
                    transport = entry.Transport,
                    bytes = entry.Data.Length,
                    hex = Convert.ToHexString(entry.Data),
                    ascii = ToSafeAscii(entry.Data)
                };

                await _jsonWriter!.WriteLineAsync(JsonSerializer.Serialize(record));
                await _jsonWriter.FlushAsync();
                await _rawStream.FlushAsync();
            }

            await _rawStream!.FlushAsync();
            await _jsonWriter!.FlushAsync();
        }
        catch (Exception ex)
        {
            DecoderDiagnostics.Write("TRAFFIC LOG ERROR", "Traffic writer failed", ex);
            LoggingError?.Invoke(ex);
        }
    }

    public static string ToSafeAscii(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(data.Length);

        foreach (var b in data)
        {
            if (b is >= 32 and <= 126)
                sb.Append((char)b);
            else if (b == 13)
                sb.Append(@"\r");
            else if (b == 10)
                sb.Append(@"\n");
            else if (b == 9)
                sb.Append(@"\t");
            else
                sb.Append('.');
        }

        return sb.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _queue.Writer.TryComplete();

        if (_writerTask is not null)
        {
            try { await _writerTask; } catch { }
        }

        if (_jsonWriter is not null)
            await _jsonWriter.DisposeAsync();

        if (_rawStream is not null)
            await _rawStream.DisposeAsync();

        _jsonWriter = null;
        _rawStream = null;
    }

    private sealed record LogEntry(string Transport, byte[] Data, DateTimeOffset Timestamp);
}
