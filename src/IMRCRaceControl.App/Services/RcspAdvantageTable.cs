using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace IMRCRaceControl.Services;

/// <summary>
/// Read-only decoder for the fixed-record Advantage ADT layout observed in RC Scoring Pro.
/// Unknown field types are exposed as bytes, not guessed. .adi indexes and .adm memo
/// payloads are never changed or executed. Imported records must still be validated.
/// </summary>
public sealed class RcspAdvantageTable
{
    public sealed record Field(string Name, byte Type, int Offset, int Length);
    private readonly byte[] _buffer;
    private readonly int _dataOffset;
    private readonly int _recordSize;
    public string Name { get; }
    public int PhysicalRecordCount { get; }
    public IReadOnlyList<Field> Fields { get; }

    private RcspAdvantageTable(string name, byte[] buffer, int count,
        IReadOnlyList<Field> fields, int dataOffset, int recordSize)
    {
        Name = name; _buffer = buffer; PhysicalRecordCount = count;
        Fields = fields; _dataOffset = dataOffset; _recordSize = recordSize;
    }

    public static RcspAdvantageTable Read(string name, byte[] bytes)
    {
        if (bytes.Length < 400 || !bytes.AsSpan(0, 15).SequenceEqual("Advantage Table"u8))
            throw new InvalidDataException($"{name}: not an Advantage ADT table.");
        var number = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24, 4));
        if (number > 5_000_000) throw new InvalidDataException($"{name}: unreasonable record count.");
        var fields = new List<Field>();
        var offset = 400;
        while (offset + 200 <= bytes.Length)
        {
            var desc = bytes.AsSpan(offset, 200);
            var terminator = desc[..128].IndexOf((byte)0);
            if (terminator <= 0) break;
            var fieldName = Encoding.Latin1.GetString(desc[..terminator]);
            if (fieldName.Any(c => c < ' ' || c > '~')) break;
            var position = BinaryPrimitives.ReadUInt32LittleEndian(desc.Slice(131, 4));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(desc.Slice(135, 4));
            if (position > 1_000_000 || length > 1_000_000 ||
                position + length > 1_000_000 ||
                fields.Any(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"{name}: invalid ADT field descriptor.");
            fields.Add(new(fieldName, desc[129], (int)position, (int)length));
            offset += 200;
            if (fields.Count > 1024) throw new InvalidDataException($"{name}: too many ADT fields.");
        }
        if (fields.Count == 0) throw new InvalidDataException($"{name}: no ADT fields found.");
        var recordSize = fields.Max(f => f.Offset + f.Length);
        if (recordSize < 5 || (long)offset + (long)number * recordSize != bytes.Length)
            throw new InvalidDataException($"{name}: record count, layout, or file length mismatch.");
        return new(name, bytes, (int)number, fields, offset, recordSize);
    }

    public IEnumerable<RcspAdvantageRow> ActiveRows()
    {
        for (var index = 0; index < PhysicalRecordCount; index++)
        {
            var start = _dataOffset + index * _recordSize;
            // Advantage marks a live record with 0x04; deleted records
            // are excluded from all previews and conversion counts.
            if (_buffer[start] != 4) continue;
            yield return new RcspAdvantageRow(
                _buffer.AsMemory(start, _recordSize), Fields, index + 1);
        }
    }

    public static RcspAdvantageTable FromZip(ZipArchive archive, string name)
    {
        var matches = archive.Entries.Where(e => e.FullName.Replace('\\', '/')
                .EndsWith("/Data/" + name + ".adt", StringComparison.OrdinalIgnoreCase) &&
            !e.FullName.Contains("/Blank/", StringComparison.OrdinalIgnoreCase) &&
            !e.FullName.Contains("/Backup/", StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1)
            throw new InvalidDataException($"Required RCSP table {name}.adt is missing or ambiguous.");
        var entry = matches[0];
        if (entry.Length > 128L * 1024 * 1024)
            throw new InvalidDataException("RCSP table exceeds the safe preview size.");
        using var source = entry.Open();
        using var target = new MemoryStream();
        source.CopyTo(target);
        if (target.Length != entry.Length)
            throw new InvalidDataException("Incomplete RCSP table content.");
        return Read(name, target.ToArray());
    }
}

public sealed class RcspAdvantageRow
{
    private readonly ReadOnlyMemory<byte> _bytes;
    private readonly Dictionary<string, RcspAdvantageTable.Field> _fields;
    public int PhysicalIndex { get; }
    internal RcspAdvantageRow(ReadOnlyMemory<byte> bytes, IReadOnlyList<RcspAdvantageTable.Field> fields, int index)
    {
        _bytes = bytes;
        PhysicalIndex = index;
        _fields = fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
    }

    private (byte Type, ReadOnlyMemory<byte> Value)? Raw(string name)
    {
        if (!_fields.TryGetValue(name, out var field)) return null;
        return (field.Type, _bytes.Slice(field.Offset, field.Length));
    }

    public string Text(string name)
    {
        var source = Raw(name);
        return source is { Type: 4 or 20 } value
            ? Encoding.Latin1.GetString(value.Value.Span).TrimEnd('\0', ' ').Trim()
            : "";
    }

    public int? Integer(string name)
    {
        var source = Raw(name);
        if (source is not { } v) return null;
        if (v.Type is 11 or 15 && v.Value.Length == 4)
        {
            var n = BinaryPrimitives.ReadInt32LittleEndian(v.Value.Span);
            return n == int.MinValue ? null : n;
        }
        if (v.Type == 12 && v.Value.Length == 2)
        {
            var n = BinaryPrimitives.ReadInt16LittleEndian(v.Value.Span);
            return n == short.MinValue ? null : n;
        }
        return null;
    }

    public double? Number(string name)
    {
        var source = Raw(name);
        if (source is not { } v || v.Type != 10 || v.Value.Length != 8) return Integer(name);
        var number = BinaryPrimitives.ReadDoubleLittleEndian(v.Value.Span);
        if (!double.IsFinite(number) || (number < 0 && Math.Abs(number) < 1e-300))
            return null;
        return number;
    }

    public DateOnly? Date(string name)
    {
        var source = Raw(name);
        if (source is not { } v || v.Type != 3 || v.Value.Length != 4) return null;
        var day = BinaryPrimitives.ReadUInt32LittleEndian(v.Value.Span);
        // Advantage ADT DATE stores Julian day numbers.
        var dayNumber = (long)day - 1721426;
        return dayNumber is >= 0 and <= 3652058
            ? DateOnly.FromDayNumber((int)dayNumber) : null;
    }

    public TimeSpan? Time(string name)
    {
        var source = Raw(name);
        if (source is not { } v || v.Type != 13 || v.Value.Length != 4) return null;
        var milliseconds = BinaryPrimitives.ReadUInt32LittleEndian(v.Value.Span);
        return milliseconds < 86_400_000 ? TimeSpan.FromMilliseconds(milliseconds) : null;
    }
}
