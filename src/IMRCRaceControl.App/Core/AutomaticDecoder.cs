using System.Globalization;
using System.Text;

namespace IMRCRaceControl.Core;

/// <summary>Passive protocol recognition on the selected transport. Never treats arbitrary digits as IDs.</summary>
public sealed class AutomaticDecoder(bool trackmateMode = false)
{
    private readonly P3StreamDecoder _p3 = new();
    private readonly StringBuilder _line = new();
    private bool _binary;
    private bool _discardLine;
    private int _amb20Epoch;
    private int _iLapEpoch;
    private int _binaryLength;
    private int _trackmateEpoch;
    private readonly Dictionary<uint, ulong> _trackmateClocks = new();
    private readonly HashSet<string> _seen = new();
    private readonly Queue<string> _recent = new();
    private readonly Dictionary<string, (ulong Clock, DateTimeOffset Local)> _anchors = new();
    public string Protocol { get; private set; } = "Auto — waiting for data";

    public void ResetConnection()
    {
        _p3.ResetConnection(); _line.Clear(); _anchors.Clear(); _binary = false; _discardLine = false;
        if (trackmateMode) { _seen.Clear(); _recent.Clear(); _trackmateClocks.Clear(); _trackmateEpoch++; }
        Protocol = trackmateMode ? "Trackmate — waiting for data" : "Auto — waiting for data";
    }

    public DecoderReadResult Feed(ReadOnlySpan<byte> bytes, DateTimeOffset receivedAt)
    {
        var p3 = trackmateMode ? new DecoderReadResult([], "Raw data", "Trackmate") : _p3.Feed(bytes, receivedAt);
        var passings = p3.Passings.ToList();
        var notes = new List<string>();
        if (p3.Summary.Contains("P3 status") || p3.Passings.Count > 0 || p3.Summary.Contains("P3 duplicate"))
            Protocol = "AMB/MyLaps P3";
        if (!p3.Summary.StartsWith("Raw data")) notes.Add(p3.Summary);
        foreach (var value in bytes)
        {
            if (!trackmateMode && value == 0x8e) { _binary = true; _binaryLength = 0; _line.Clear(); }
            if (_binary)
            {
                if (value == 0x8f || ++_binaryLength > 65535) _binary = false;
                continue;
            }
            if (value == 1) { _discardLine = false; _line.Clear(); _line.Append((char)value); continue; }
            if (value is 10 or 13)
            {
                if (_line.Length > 0) ReadLine(_line.ToString(), passings, notes, receivedAt);
                _line.Clear(); _discardLine = false;
            }
            else if (_discardLine) continue;
            else if (value is 9 or >= 32 and <= 126)
            {
                if (_line.Length < 512) _line.Append((char)value);
                else { _line.Clear(); _discardLine = true; }
            }
            else _line.Clear();
        }
        return new(passings.OrderBy(p => p.Timestamp).ToList(),
            notes.Count > 0 ? string.Join("; ", notes.Distinct()) :
            $"{Protocol} — waiting for a supported complete frame", Protocol);
    }

    private void ReadLine(string line, List<DecoderPassing> passings, List<string> notes, DateTimeOffset now)
    {
        if (trackmateMode) { ReadTrackmate(line, passings, notes, now); return; }
        if (line.Length > 0 && line[0] == 1)
        {
            var text = line[1..];
            var fields = text.Split('\t');
            // I-Lap's seven-digit protocol is SOH/tab framed, without enhanced CRC.
            // Its exact four-field shapes keep it separate from AMB enhanced.
            if (fields.Length == 4 && fields[0] == "%" && U(fields[1], out var initializedDecoder) &&
                fields[2] == "0" && fields[3] == "0")
            {
                Protocol = "I-Lap"; _iLapEpoch++; _anchors.Remove($"I-Lap:{initializedDecoder}");
                notes.Add("I-Lap initialized — no crossing"); return;
            }
            if (fields.Length == 4 && fields[0] == "@")
            {
                if (!U(fields[1], out var iLapDecoder) || fields[2].Length != 7 || !U(fields[2], out var iLapTag) ||
                    iLapTag == 0 || !decimal.TryParse(fields[3], NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var iLapSeconds) || iLapSeconds < 0 || iLapSeconds > 604800)
                { notes.Add("Invalid I-Lap passing"); return; }
                Protocol = "I-Lap";
                Add(iLapTag, iLapDecoder, 0, (ulong)(iLapSeconds * 1000000), null, null,
                    "I-Lap", passings, notes, now, fields[2]);
                return;
            }
            if (fields.Length < 2 || fields[^1].Length != 5 || fields[^1][0] != 'x' ||
                !ushort.TryParse(fields[^1].AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var checksum))
            { notes.Add("Unrecognized legacy record"); return; }
            ushort crc = 0xffff;
            foreach (var b in Encoding.ASCII.GetBytes(text[..^5]))
            {
                for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc << 1) ^ ((crc & 0x8000) != 0 ? 0x1021 : 0));
                crc ^= b;
            }
            if (crc != checksum) { notes.Add("AMB enhanced checksum failed"); return; }
            Protocol = "AMB enhanced text";
            if (fields[0] != "@") { notes.Add("AMB enhanced status — no crossing"); return; }
            if (fields.Length != 9 || !U(fields[1], out var decoder) || !U(fields[2], out var sequence) ||
                !U(fields[3], out var tag) || tag == 0 ||
                !decimal.TryParse(fields[4], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) ||
                seconds < 0 || seconds > 604800 || !U(fields[5], out var hits) || !U(fields[6], out var strength))
            { notes.Add("Invalid AMB enhanced passing"); return; }
            Add(tag, decoder, sequence, (ulong)(seconds * 1000000), (int)Math.Min(hits, int.MaxValue),
                (int)Math.Min(strength, int.MaxValue), "AMB enhanced", passings, notes, now);
            return;
        }
        if (line == "@00000001") { Protocol = "AMB20"; _amb20Epoch++; _anchors.Remove("AMB20:0"); notes.Add("AMB20 initialization"); return; }
        if (line.Length != 11 || line[0] != '@' || !line.AsSpan(1).ToArray().All(char.IsAsciiDigit)) return;
        var id = uint.Parse(line.AsSpan(1, 2), CultureInfo.InvariantCulture);
        var hours = uint.Parse(line.AsSpan(3, 2), CultureInfo.InvariantCulture);
        var minutes = uint.Parse(line.AsSpan(5, 2), CultureInfo.InvariantCulture);
        var seconds20 = uint.Parse(line.AsSpan(7, 2), CultureInfo.InvariantCulture);
        var hundredths = uint.Parse(line.AsSpan(9, 2), CultureInfo.InvariantCulture);
        if (minutes > 59 || seconds20 > 59) { notes.Add("Invalid AMB20 clock"); return; }
        Protocol = "AMB20";
        if (id == 0) { notes.Add("AMB20 clock — no crossing"); return; }
        var micros = (hours * 3600UL + minutes * 60UL + seconds20) * 1000000 + hundredths * 10000UL;
        Add(id, 0, 0, micros, null, null, "AMB20", passings, notes, now);
    }

    private void ReadTrackmate(string line, List<DecoderPassing> passings, List<string> notes, DateTimeOffset now)
    {
        if (line.Length < 2 || line[0] != 1) return;
        var fields = line[1..].Split('\t');
        if (fields[0] is "#" or "%" or "?")
        {
            Protocol = "Trackmate";
            notes.Add("Trackmate status — no crossing");
            return;
        }
        if (fields[0] != "@") return;
        // RC AMB-style positions: marker, decoder, passing counter, tag, seconds.
        // Do not use the four-field I-Lap layout or interpret status digits as tags.
        if (fields.Length is < 5 or > 9 || !U(fields[1], out var decoder) || !U(fields[2], out var number) ||
            fields[3].Length is < 1 or > 10 || !U(fields[3], out var tag) || tag == 0 ||
            !decimal.TryParse(fields[4], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) ||
            seconds < 0 || seconds > 604800)
        { notes.Add("Invalid Trackmate passing"); return; }
        int? hits = null;
        // Optional diagnostic fields are not used to determine identity or lap timing.
        // Trackmate variants do not provide a documented reliable AMB enhanced CRC.
        for (var i = 5; i < fields.Length; i++)
        {
            var field = fields[i];
            if (field.Length == 0) continue;
            if (i == fields.Length - 1 && field.Length == 5 && field[0] == 'x' &&
                ushort.TryParse(field.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) continue;
            if (!U(field, out var diagnostic)) { notes.Add("Invalid Trackmate diagnostics"); return; }
            if (i == 5) hits = (int)Math.Min(diagnostic, int.MaxValue);
        }
        var clock = (ulong)(seconds * 1000000);
        if (_trackmateClocks.TryGetValue(decoder, out var previous) && clock + 2000000UL < previous)
        {
            _trackmateEpoch++;
            _anchors.Remove($"Trackmate:{decoder}");
            notes.Add("Trackmate clock restarted");
        }
        _trackmateClocks[decoder] = clock;
        Protocol = "Trackmate";
        Add(tag, decoder, number, clock, hits, null, "Trackmate", passings, notes, now, fields[3]);
    }

    private void Add(uint tag, uint decoder, uint number, ulong clock, int? hits, int? strength,
        string protocol, List<DecoderPassing> passings, List<string> notes, DateTimeOffset now, string? transponderText = null)
    {
        var key = $"{protocol}:{decoder}";
        var identity = $"{key}:{(protocol == "AMB20" ? _amb20Epoch : protocol == "I-Lap" ? _iLapEpoch : protocol == "Trackmate" ? _trackmateEpoch : 0)}:{number}:{clock}:{tag}";
        if (!_seen.Add(identity)) { notes.Add($"{protocol} duplicate {tag}"); return; }
        _recent.Enqueue(identity);
        while (_recent.Count > 4096) _seen.Remove(_recent.Dequeue());
        if (!_anchors.TryGetValue(key, out var anchor) || clock < anchor.Clock ||
            clock - anchor.Clock > 604800000000UL)
            anchor = (clock, now);
        var time = anchor.Local.AddTicks((long)(clock - anchor.Clock) * 10);
        if (time > now.AddSeconds(2) || time < now.AddMinutes(-1))
        {
            // If several records arrived in one read, move this batch together.
            var shift = now - time;
            for (var i = 0; i < passings.Count; i++)
                if (passings[i].Protocol == protocol && passings[i].DecoderId == decoder)
                    passings[i] = passings[i] with { Timestamp = passings[i].Timestamp + shift };
            anchor = (clock, now); time = now;
        }
        _anchors[key] = anchor;
        passings.Add(new(transponderText ?? tag.ToString(CultureInfo.InvariantCulture), time, decoder, number, clock, hits, strength, protocol));
        notes.Add($"{protocol} transponder {tag}");
    }
    private static bool U(string value, out uint result) => uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
}
