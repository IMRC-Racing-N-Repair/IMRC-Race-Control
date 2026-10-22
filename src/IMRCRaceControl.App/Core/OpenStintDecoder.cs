using System.Globalization;
using System.Text;

namespace IMRCRaceControl.Core;

/// <summary>One complete ZeroMQ message per call; timestamp is milliseconds, not microseconds.</summary>
public sealed class OpenStintDecoder
{
    private ulong? _lastClock;
    private DateTimeOffset _lastTime;
    private readonly HashSet<string> _seen = new();
    private readonly Queue<string> _recent = new();
    public void ResetConnection(bool clearHistory = false)
    {
        _lastClock = null;
        if (clearHistory) { _seen.Clear(); _recent.Clear(); }
    }

    public DecoderReadResult FeedMessage(ReadOnlySpan<byte> bytes, DateTimeOffset receivedAt)
    {
        DecoderReadResult Empty(string message) => new([], message, "OpenStint");
        if (bytes.Length == 0 || bytes.Length > 4096) return Empty("Invalid OpenStint message length");
        var text = Encoding.ASCII.GetString(bytes).TrimEnd('\r', '\n');
        if (text.Contains('\r') || text.Contains('\n')) return Empty("Invalid OpenStint message framing");
        var fields = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2 || !ulong.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var clock))
            return Empty("Invalid OpenStint clock");
        if (fields[0] is "S" or "T" or "L") return Empty($"OpenStint {fields[0]} diagnostic — no crossing");
        if (fields.Length < 7 || fields[0] != "P" || fields[2] is not ("OPN" or "AMB" or "RC4") ||
            !uint.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var tag) ||
            !double.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var rssi) || !double.IsFinite(rssi) ||
            !uint.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out var hits) ||
            !double.TryParse(fields[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var durationOrEvm) ||
            !double.IsFinite(durationOrEvm) || durationOrEvm < 0 || clock > ulong.MaxValue / 1000)
            return Empty("Invalid OpenStint passing");
        var identity = $"{clock}:{fields[2]}:{tag}";
        if (_seen.Contains(identity)) return Empty($"OpenStint duplicate {tag}");
        var time = receivedAt;
        if (_lastClock.HasValue && clock >= _lastClock.Value && clock - _lastClock.Value <= 604800000)
        {
            time = _lastTime.AddMilliseconds(clock - _lastClock.Value);
            if (time > receivedAt.AddSeconds(2) || time < receivedAt.AddMinutes(-1)) time = receivedAt;
        }
        else if (_lastClock.HasValue && clock < _lastClock.Value)
        {
            _seen.Clear(); _recent.Clear(); // decoder process clock restarted
        }
        _lastClock = clock; _lastTime = time;
        _seen.Add(identity); _recent.Enqueue(identity);
        while (_recent.Count > 4096) _seen.Remove(_recent.Dequeue());
        // RSSI is a signed floating-point dB value; retain it in diagnostics, not an integer signal field.
        var passing = new DecoderPassing(tag.ToString(CultureInfo.InvariantCulture), time, 0, 0, clock * 1000,
            (int)Math.Min(hits, int.MaxValue), null, "OpenStint");
        return new([passing], $"OpenStint {fields[2]} transponder {tag} • RSSI {fields[4]} dB • hits {hits}", "OpenStint");
    }
}
