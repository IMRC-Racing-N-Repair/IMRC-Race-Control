using System.Buffers.Binary;
using System.IO.Compression;
using IMRCRaceControl.Services;

public static class RaceImportChecks
{
    public static int Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "race-import-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var driver = Sample("Driver", ("DriverID", (byte)11, 4), ("FullName", (byte)4, 24));
            var table = RcspAdvantageTable.Read("Driver", driver);
            if (table.PhysicalRecordCount != 2 || table.ActiveRows().Count() != 1 ||
                table.ActiveRows().Single().Integer("DriverID") != 123 ||
                table.ActiveRows().Single().Text("FullName") != "Example Racer")
                throw new Exception("Advantage reader did not preserve live records or field values.");
            var zipPath = Path.Combine(dir, "rcsp.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var name in new[] { "Driver", "Classes", "Entries" })
                {
                    var entry = zip.CreateEntry("RCSP Data/Data/" + name + ".adt");
                    using var stream = entry.Open();
                    stream.Write(Sample(name, ("DriverID", (byte)11, 4), ("FullName", (byte)4, 24)));
                }
                // Copies in Blank/ must never be used as active source tables.
                using var blank = zip.CreateEntry("RCSP Data/Data/Blank/Driver.adt").Open();
                blank.Write(new byte[15]);
            }
            var found = RaceImportDetector.Inspect(zipPath);
            if (found.Format != RaceImportFormat.RcScoringPro ||
                !found.Notes.Any(n => n.StartsWith("Driver:", StringComparison.Ordinal)))
                throw new Exception("RCSP ZIP was not identified or validated.");
            var unknown = Path.Combine(dir, "unknown.bin");
            File.WriteAllText(unknown, "not a race database");
            if (RaceImportDetector.Inspect(unknown).Format != RaceImportFormat.Unknown)
                throw new Exception("Unknown files must not be misidentified as LiveTime.");
            return 3;
        }
        finally { Directory.Delete(dir, true); }
    }

    private static byte[] Sample(string name, params (string Name, byte Type, int Size)[] fields)
    {
        var rowSize = 5 + fields.Sum(f => f.Size);
        var data = new byte[400 + fields.Length * 200 + rowSize * 2];
        "Advantage Table"u8.CopyTo(data.AsSpan(0, 15));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24, 4), 2);
        var offset = 5;
        for (var i = 0; i < fields.Length; i++)
        {
            var f = fields[i];
            System.Text.Encoding.ASCII.GetBytes(f.Name)
                .CopyTo(data, 400 + i * 200);
            data[400 + i * 200 + 129] = f.Type;
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(400 + i * 200 + 131, 4), offset);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(400 + i * 200 + 135, 4), f.Size);
            offset += f.Size;
        }
        var start = 400 + fields.Length * 200;
        data[start] = 4;
        data[start + rowSize] = 5;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(start + 5, 4), 123);
        System.Text.Encoding.ASCII.GetBytes("Example Racer").CopyTo(data, start + 9);
        return data;
    }
}
