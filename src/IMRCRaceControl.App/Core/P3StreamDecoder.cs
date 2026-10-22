using System.Buffers.Binary;
using System.Globalization;

namespace IMRCRaceControl.Core;

public sealed record DecoderPassing(string TransponderId, DateTimeOffset Timestamp,
    uint DecoderId, uint PassingNumber, ulong ClockMicroseconds, int? Hits, int? Strength, string Protocol = "P3");

public sealed record DecoderReadResult(IReadOnlyList<DecoderPassing> Passings, string Summary, string Protocol = "P3");

/// <summary>
/// Passive AMB/MyLaps P3 v2 stream reader. No driver database is involved in
/// decoding. Transport read boundaries are unrelated to protocol boundaries.
/// </summary>
public sealed class P3StreamDecoder
{
    private const int MaximumFrameLength = 65535;
    private readonly List<byte> _frame = new();
    private readonly HashSet<(uint Decoder, uint Number, ulong Clock, uint Tag)> _seen = new();
    private readonly Queue<(uint Decoder, uint Number, ulong Clock, uint Tag)> _recent = new();
    private readonly Dictionary<uint, (ulong Clock, DateTimeOffset Local, ulong LastClock)> _clocks = new();
    private bool _collecting;
    private bool _escaped;

    // Reconnection drops partial bytes and clock anchors, but retains recent
    // passing identities so a decoder retransmission cannot score twice.
    public void ResetConnection()
    {
        _frame.Clear();
        _collecting = _escaped = false;
        _clocks.Clear();
    }

    public DecoderReadResult Feed(ReadOnlySpan<byte> bytes, DateTimeOffset receivedAt)
    {
        var passings = new List<DecoderPassing>();
        var notes = new List<string>();
        foreach (var value in bytes)
        {
            if (value == 0x8e)
            {
                if (_collecting) notes.Add("Incomplete P3 frame discarded");
                _frame.Clear();
                _frame.Add(value);
                _collecting = true;
                _escaped = false;
                continue;
            }
            if (!_collecting) continue;
            if (_escaped)
            {
                _escaped = false;
                if (value is < 0xad or > 0xaf)
                {
                    notes.Add("Invalid P3 escape");
                    _collecting = false;
                    _frame.Clear();
                    continue;
                }
                _frame.Add((byte)(value - 0x20));
            }
            else if (value == 0x8d) _escaped = true;
            else if (value == 0x8f)
            {
                _frame.Add(value);
                ReadFrame(_frame.ToArray(), receivedAt, passings, notes);
                _frame.Clear();
                _collecting = false;
            }
            else _frame.Add(value);

            if (_frame.Count >= MaximumFrameLength)
            {
                notes.Add("Oversized P3 frame discarded");
                _collecting = _escaped = false;
                _frame.Clear();
            }
        }
        MapDecoderTimes(passings, receivedAt);
        if (notes.Count == 0)
            notes.Add(_collecting ? "Waiting for complete P3 frame" : "Raw data — no P3 frame");
        return new(passings, string.Join("; ", notes.Distinct()));
    }

    private void ReadFrame(byte[] data, DateTimeOffset receivedAt,
        List<DecoderPassing> passings, List<string> notes)
    {
        if (data.Length < 11 || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2)) != data.Length)
        { notes.Add("Invalid P3 length"); return; }
        if (data[1] != 2) { notes.Add($"Unsupported P3 version {data[1]}"); return; }
        if (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4)) != CalculateChecksum(data))
        { notes.Add("P3 checksum failed"); return; }
        var kind = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8));
        if (kind != 1)
        {
            notes.Add(kind == 2 ? "P3 status — no crossing" : $"P3 record 0x{kind:X4} — no crossing");
            return;
        }

        uint? tag = null, number = null, decoder = null;
        ulong? clock = null;
        int? hits = null, strength = null;
        var fields = new HashSet<byte>();
        for (var offset = 10; offset < data.Length - 1;)
        {
            if (offset + 2 > data.Length - 1) { notes.Add("Invalid P3 field"); return; }
            var id = data[offset++];
            var size = data[offset++];
            if (offset + size > data.Length - 1 || !fields.Add(id))
            { notes.Add("Invalid P3 field"); return; }
            var field = data.AsSpan(offset, size);
            switch (id)
            {
                case 1 when size == 4: number = BinaryPrimitives.ReadUInt32LittleEndian(field); break;
                case 3 when size == 4: tag = BinaryPrimitives.ReadUInt32LittleEndian(field); break;
                case 4 when size == 8: clock = BinaryPrimitives.ReadUInt64LittleEndian(field); break;
                case 5 when size == 2: strength = BinaryPrimitives.ReadUInt16LittleEndian(field); break;
                case 6 when size == 2: hits = BinaryPrimitives.ReadUInt16LittleEndian(field); break;
                case 0x81 when size == 4: decoder = BinaryPrimitives.ReadUInt32LittleEndian(field); break;
                case 1 or 3 or 4 or 5 or 6 or 0x81: notes.Add("Invalid P3 field size"); return;
            }
            offset += size;
        }
        if (tag is null or 0 || number is null || clock is null || decoder is null)
        { notes.Add("P3 passing missing required fields"); return; }
        var identity = (decoder.Value, number.Value, clock.Value, tag.Value);
        if (!_seen.Add(identity)) { notes.Add($"P3 duplicate {tag.Value}"); return; }
        _recent.Enqueue(identity);
        while (_recent.Count > 4096) _seen.Remove(_recent.Dequeue());

        passings.Add(new(tag.Value.ToString(CultureInfo.InvariantCulture), receivedAt,
            decoder.Value, number.Value, clock.Value, hits, strength));
        notes.Add($"P3 transponder {tag.Value}");
    }

    private void MapDecoderTimes(List<DecoderPassing> passings, DateTimeOffset receivedAt)
    {
        // Anchor the latest record in the first batch to receipt time. Earlier
        // records in the same TCP read retain their spacing, rather than being
        // assigned identical receipt times. No assumption about the RTC epoch.
        foreach (var group in passings.GroupBy(p => p.DecoderId))
        {
            var latest = group.Max(p => p.ClockMicroseconds);
            var found = _clocks.TryGetValue(group.Key, out var anchor);
            var elapsed = (decimal)latest - anchor.Clock;
            if (!found || latest < anchor.LastClock || Math.Abs(elapsed) > 604800000000m ||
                anchor.Local.AddTicks((long)(elapsed * 10)) > receivedAt.AddSeconds(2) ||
                anchor.Local.AddTicks((long)(elapsed * 10)) < receivedAt.AddMinutes(-1))
                anchor = (latest, receivedAt, latest);
            _clocks[group.Key] = (anchor.Clock, anchor.Local, latest);
        }
        for (var index = 0; index < passings.Count; index++)
        {
            var passing = passings[index];
            var anchor = _clocks[passing.DecoderId];
            var delta = (decimal)passing.ClockMicroseconds - anchor.Clock;
            var timestamp = Math.Abs(delta) <= 604800000000m
                ? anchor.Local.AddTicks((long)(delta * 10)) : receivedAt;
            passings[index] = passing with { Timestamp = timestamp };
        }
    }

    private static ushort CalculateChecksum(ReadOnlySpan<byte> data)
    {
        // P3 CRC: non-reflected 0x1021, seed FFFF, each byte enters the low
        // bits after shifting. Include delimiters and zero the CRC field.
        ushort crc = 0xffff;
        for (var index = 0; index < data.Length; index++)
        {
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc << 1) ^ ((crc & 0x8000) != 0 ? 0x1021 : 0));
            if (index is not 4 and not 5) crc ^= data[index];
        }
        return crc;
    }
}
