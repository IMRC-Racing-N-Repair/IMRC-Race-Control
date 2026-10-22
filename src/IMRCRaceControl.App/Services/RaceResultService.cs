using System.IO;
using System.Text;
using System.Text.Json;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Services;

public sealed class RaceResultService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string ResultsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IMRCRaceControl",
        "Results");

    public async Task<(string JsonPath, string CsvPath)> SaveAsync(
        string eventName,
        string className,
        string sessionName,
        SessionType sessionType,
        StartStyle startStyle,
        DateTimeOffset? startedAt,
        TimeSpan duration,
        IReadOnlyList<StandingRow> standings,
        IReadOnlyList<LapRecord> laps,
        IReadOnlyList<BreakoutRecord>? breakouts = null)
    {
        Directory.CreateDirectory(ResultsDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var safeClass = MakeSafeFilePart(string.IsNullOrWhiteSpace(className) ? "Race" : className);
        var safeSession = MakeSafeFilePart(string.IsNullOrWhiteSpace(sessionName) ? sessionType.ToString() : sessionName);
        var baseName = $"{stamp}_{safeClass}_{safeSession}";
        var jsonPath = Path.Combine(ResultsDirectory, baseName + ".json");
        var csvPath = Path.Combine(ResultsDirectory, baseName + ".csv");

        var document = new
        {
            eventName,
            className,
            sessionName,
            sessionType = sessionType.ToString(),
            startStyle = startStyle.ToDisplay(),
            startedAt,
            durationSeconds = duration.TotalSeconds,
            savedAt = DateTimeOffset.Now,
            standings,
            laps,
            breakouts = breakouts ?? []
        };

        await using (var stream = File.Create(jsonPath))
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions);

        var sb = new StringBuilder();
        sb.AppendLine("Position,Car,Driver,Laps,Clock,Last Lap,Best Lap,Best Lap #,Top 2,Top 3,Top 5,Average,Avg 5,Avg 10,Avg 15,Consistency,Pace,Gap Leader,Gap Behind,Total Time,Status");
        foreach (var row in standings)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                row.Position.ToString(),
                row.CarNumber.ToString(),
                Csv(row.DriverName),
                row.Laps.ToString(),
                Csv(row.Clock),
                Csv(row.LastLap),
                Csv(row.BestLap),
                Csv(row.BestLapNumber),
                Csv(row.Top2),
                Csv(row.Top3),
                Csv(row.Top5),
                Csv(row.AverageLap),
                Csv(row.Average5),
                Csv(row.Average10),
                Csv(row.Average15),
                Csv(row.Consistency),
                Csv(row.Pace),
                Csv(row.GapToLeader),
                Csv(row.GapBehind),
                Csv(row.TotalTime),
                Csv(row.Status)
            }));
        }

        sb.AppendLine();
        sb.AppendLine("Lap Detail");
        sb.AppendLine("Sequence,Car,Driver,Lap,Lap Time,Elapsed,Source,Finished,Timestamp");
        foreach (var lap in laps.OrderBy(l => l.Sequence))
        {
            sb.AppendLine(string.Join(",", new[]
            {
                lap.Sequence.ToString(),
                lap.CarNumber.ToString(),
                Csv(lap.DriverName),
                lap.LapNumber.ToString(),
                Csv(IMRCRaceControl.Core.RaceEngine.FormatLap(lap.LapTime)),
                Csv(IMRCRaceControl.Core.RaceEngine.FormatElapsed(lap.Elapsed)),
                Csv(lap.Source),
                lap.Finished.ToString(),
                Csv(lap.Timestamp.ToString("O"))
            }));
        }

        sb.AppendLine();
        sb.AppendLine("Breakout / Below-Minimum Observations");
        sb.AppendLine("Sequence,Car,Driver,Attempted Lap,Lap Time,Elapsed,Minimum Allowed,Source,Timestamp");
        foreach (var breakout in (breakouts ?? []).OrderBy(b => b.Timestamp).ThenBy(b => b.Sequence))
        {
            sb.AppendLine(string.Join(",", new[]
            {
                breakout.Sequence.ToString(),
                breakout.CarNumber.ToString(),
                Csv(breakout.DriverName),
                breakout.AttemptedLapNumber.ToString(),
                Csv(IMRCRaceControl.Core.RaceEngine.FormatLap(breakout.LapTime)),
                Csv(IMRCRaceControl.Core.RaceEngine.FormatElapsed(breakout.Elapsed)),
                Csv(IMRCRaceControl.Core.RaceEngine.FormatLap(breakout.MinimumLapTime)),
                Csv(breakout.Source),
                Csv(breakout.Timestamp.ToString("O"))
            }));
        }

        await File.WriteAllTextAsync(csvPath, sb.ToString(), new UTF8Encoding(true));
        return (jsonPath, csvPath);
    }

    private static string Csv(string value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    private static string MakeSafeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Length > 50 ? cleaned[..50] : cleaned;
    }
}
