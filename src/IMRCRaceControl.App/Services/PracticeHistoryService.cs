using Microsoft.Data.Sqlite;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Services;

public sealed class PracticeHistoryService
{
    public string DatabasePath { get; }
    public PracticeHistoryService(string? databasePath=null) => DatabasePath=databasePath??ApplicationDataPaths.DatabasePath;

    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared
    }.ToString();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        var pragmas = connection.CreateCommand();
        pragmas.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        await pragmas.ExecuteNonQueryAsync();

        var schema = connection.CreateCommand();
        schema.CommandText = """
            CREATE TABLE IF NOT EXISTS PracticeStints (
                StintId TEXT PRIMARY KEY,
                DriverId TEXT NOT NULL,
                DriverName TEXT NOT NULL,
                TransponderId TEXT NOT NULL,
                StartedAt TEXT NOT NULL,
                EndedAt TEXT NOT NULL,
                LapCount INTEGER NOT NULL,
                BestLapSeconds REAL NULL,
                AverageLapSeconds REAL NULL
            );

            CREATE TABLE IF NOT EXISTS PracticeLaps (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StintId TEXT NOT NULL,
                LapNumber INTEGER NOT NULL,
                LapTimeSeconds REAL NOT NULL,
                Timestamp TEXT NOT NULL,
                Source TEXT NOT NULL,
                FOREIGN KEY(StintId) REFERENCES PracticeStints(StintId) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS PracticeStintTracks (
                StintId TEXT PRIMARY KEY,
                TrackId TEXT NOT NULL,
                PracticeDate TEXT NOT NULL,
                FOREIGN KEY(StintId) REFERENCES PracticeStints(StintId) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_PracticeStintTracks_TrackDate
                ON PracticeStintTracks(TrackId, PracticeDate, StintId);
            CREATE INDEX IF NOT EXISTS IX_PracticeStints_StartedAt
                ON PracticeStints(StartedAt DESC);

            CREATE INDEX IF NOT EXISTS IX_PracticeLaps_StintId
                ON PracticeLaps(StintId, LapNumber);
            """;
        await schema.ExecuteNonQueryAsync();
    }

    public async Task SaveStintAsync(PracticeCardViewModel card, DateTimeOffset endedAt)
    {
        await InitializeAsync();

        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        var lapTimes = card.Laps.Select(l => l.LapTime.TotalSeconds).ToList();
        double? best = lapTimes.Count > 0 ? lapTimes.Min() : null;
        double? average = lapTimes.Count > 0 ? lapTimes.Average() : null;

        var stintCommand = connection.CreateCommand();
        stintCommand.Transaction = transaction;
        stintCommand.CommandText = """
            INSERT OR REPLACE INTO PracticeStints
                (StintId, DriverId, DriverName, TransponderId, StartedAt, EndedAt, LapCount, BestLapSeconds, AverageLapSeconds)
            VALUES
                ($id, $driverId, $driverName, $transponderId, $startedAt, $endedAt, $lapCount, $best, $average);
            """;
        stintCommand.Parameters.AddWithValue("$id", card.StintId);
        stintCommand.Parameters.AddWithValue("$driverId", card.DriverId);
        stintCommand.Parameters.AddWithValue("$driverName", card.DriverName);
        stintCommand.Parameters.AddWithValue("$transponderId", card.TransponderId);
        stintCommand.Parameters.AddWithValue("$startedAt", card.StartedAt.ToString("O"));
        stintCommand.Parameters.AddWithValue("$endedAt", endedAt.ToString("O"));
        stintCommand.Parameters.AddWithValue("$lapCount", card.LapCount);
        stintCommand.Parameters.AddWithValue("$best", (object?)best ?? DBNull.Value);
        stintCommand.Parameters.AddWithValue("$average", (object?)average ?? DBNull.Value);
        await stintCommand.ExecuteNonQueryAsync();
        if(!string.IsNullOrWhiteSpace(card.TrackId) && !string.IsNullOrWhiteSpace(card.PracticeDate))
        {
            await using var mapping=connection.CreateCommand();
            mapping.Transaction=transaction;
            mapping.CommandText="INSERT INTO PracticeStintTracks(StintId,TrackId,PracticeDate) VALUES($id,$track,$date) ON CONFLICT(StintId) DO UPDATE SET TrackId=excluded.TrackId, PracticeDate=excluded.PracticeDate";
            mapping.Parameters.AddWithValue("$id",card.StintId);
            mapping.Parameters.AddWithValue("$track",card.TrackId);
            mapping.Parameters.AddWithValue("$date",card.PracticeDate);
            await mapping.ExecuteNonQueryAsync();
        }

        var deleteLaps = connection.CreateCommand();
        deleteLaps.Transaction = transaction;
        deleteLaps.CommandText = "DELETE FROM PracticeLaps WHERE StintId = $id;";
        deleteLaps.Parameters.AddWithValue("$id", card.StintId);
        await deleteLaps.ExecuteNonQueryAsync();

        foreach (var lap in card.Laps)
        {
            var lapCommand = connection.CreateCommand();
            lapCommand.Transaction = transaction;
            lapCommand.CommandText = """
                INSERT INTO PracticeLaps
                    (StintId, LapNumber, LapTimeSeconds, Timestamp, Source)
                VALUES
                    ($stintId, $lapNumber, $lapTime, $timestamp, $source);
                """;
            lapCommand.Parameters.AddWithValue("$stintId", card.StintId);
            lapCommand.Parameters.AddWithValue("$lapNumber", lap.LapNumber);
            lapCommand.Parameters.AddWithValue("$lapTime", lap.LapTime.TotalSeconds);
            lapCommand.Parameters.AddWithValue("$timestamp", lap.Timestamp.ToString("O"));
            lapCommand.Parameters.AddWithValue("$source", lap.Source);
            await lapCommand.ExecuteNonQueryAsync();
        }

        transaction.Commit();
    }

    public async Task<IReadOnlyList<PracticeHistoryRow>> LoadRecentStintsAsync(int limit = 500, string? driverId = null)
    {
        await InitializeAsync();

        var rows = new List<PracticeHistoryRow>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT StintId, DriverId, DriverName, TransponderId, StartedAt, EndedAt,
                   LapCount, BestLapSeconds, AverageLapSeconds
            FROM PracticeStints
            WHERE ($driverId IS NULL OR DriverId = $driverId)
            ORDER BY StartedAt DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", driverId is null ? Math.Clamp(limit, 1, 5000) : -1);
        command.Parameters.AddWithValue("$driverId", (object?)driverId ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new PracticeHistoryRow
            {
                StintId = reader.GetString(0),
                DriverId = reader.GetString(1),
                DriverName = reader.GetString(2),
                TransponderId = reader.GetString(3),
                StartedAt = DateTimeOffset.Parse(reader.GetString(4)),
                EndedAt = DateTimeOffset.Parse(reader.GetString(5)),
                LapCount = reader.GetInt32(6),
                BestLapSeconds = reader.IsDBNull(7) ? null : reader.GetDouble(7),
                AverageLapSeconds = reader.IsDBNull(8) ? null : reader.GetDouble(8)
            });
        }

        return rows;
    }

    // Only explicit operator confirmation assigns pre-upgrade sessions to a venue.
    public async Task<int> AssignLegacyTrackAsync(TrackProfile track)
    {
        await InitializeAsync();
        var zone=TimeZoneInfo.FindSystemTimeZoneById(track.TimeZoneId);
        await using var connection=new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        using var transaction=connection.BeginTransaction();
        var missing=new List<(string Id,string Date)>();
        await using(var query=connection.CreateCommand())
        {
            query.Transaction=transaction;
            query.CommandText="SELECT s.StintId,s.StartedAt FROM PracticeStints s LEFT JOIN PracticeStintTracks t ON t.StintId=s.StintId WHERE t.StintId IS NULL";
            await using var reader=await query.ExecuteReaderAsync();
            while(await reader.ReadAsync())
                missing.Add((reader.GetString(0),TimeZoneInfo.ConvertTime(DateTimeOffset.Parse(reader.GetString(1)),zone).ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)));
        }
        foreach(var row in missing)
        {
            await using var cmd=connection.CreateCommand();
            cmd.Transaction=transaction;
            cmd.CommandText="INSERT OR IGNORE INTO PracticeStintTracks(StintId,TrackId,PracticeDate) VALUES($id,$track,$date)";
            cmd.Parameters.AddWithValue("$id",row.Id);
            cmd.Parameters.AddWithValue("$track",track.Id);
            cmd.Parameters.AddWithValue("$date",row.Date);
            await cmd.ExecuteNonQueryAsync();
        }
        transaction.Commit();
        return missing.Count;
    }

    public async Task<IReadOnlyList<PracticeHistoryRow>> LoadArchiveCandidatesAsync(string trackId,IReadOnlySet<string> delivered,int limit=20)
    {
        await InitializeAsync();
        var rows=new List<PracticeHistoryRow>();
        await using var connection=new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT s.StintId,s.DriverName,s.TransponderId,s.StartedAt,s.EndedAt,s.LapCount,
                   s.BestLapSeconds,s.AverageLapSeconds,t.PracticeDate
            FROM PracticeStintTracks t JOIN PracticeStints s ON s.StintId=t.StintId
            WHERE t.TrackId=$track AND s.LapCount>0
            ORDER BY t.PracticeDate,s.StartedAt,s.StintId
            """;
        command.Parameters.AddWithValue("$track",trackId);
        await using var reader=await command.ExecuteReaderAsync();
        while(rows.Count<Math.Clamp(limit,1,50) && await reader.ReadAsync())
        {
            var id=reader.GetString(0);
            if(delivered.Contains(id))continue;
            rows.Add(new PracticeHistoryRow {
                StintId=id,DriverName=reader.GetString(1),TransponderId=reader.GetString(2),
                StartedAt=DateTimeOffset.Parse(reader.GetString(3)),EndedAt=DateTimeOffset.Parse(reader.GetString(4)),
                LapCount=reader.GetInt32(5),BestLapSeconds=reader.IsDBNull(6)?null:reader.GetDouble(6),
                AverageLapSeconds=reader.IsDBNull(7)?null:reader.GetDouble(7),PracticeDate=reader.GetString(8)
            });
        }
        return rows;
    }

    public async Task<IReadOnlyList<PracticeHistoryLapRow>> LoadLapsAsync(string stintId)
    {
        await InitializeAsync();

        var rows = new List<PracticeHistoryLapRow>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT LapNumber, LapTimeSeconds, Timestamp, Source
            FROM PracticeLaps
            WHERE StintId = $stintId
            ORDER BY LapNumber;
            """;
        command.Parameters.AddWithValue("$stintId", stintId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new PracticeHistoryLapRow
            {
                LapNumber = reader.GetInt32(0),
                LapTimeSeconds = reader.GetDouble(1),
                Timestamp = DateTimeOffset.Parse(reader.GetString(2)),
                Source = reader.GetString(3)
            });
        }

        return rows;
    }
}
