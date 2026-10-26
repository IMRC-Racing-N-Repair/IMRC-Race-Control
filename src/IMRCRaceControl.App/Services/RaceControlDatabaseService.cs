using System.Text.Json;
using Microsoft.Data.Sqlite;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Services;

/// <summary>
/// Authoritative local persistence for Race Control.
///
/// The database stores the full application state plus queryable copies of the
/// core racing entities. The legacy settings.json file is retained only as a
/// fallback/migration backup; after the first successful import the SQLite
/// database is the source of truth.
/// </summary>
public sealed class RaceControlDatabaseService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private volatile bool _initialized;

    public string DatabasePath { get; } = ApplicationDataPaths.DatabasePath;

    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        Pooling = true
    }.ToString();

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        await _initializeGate.WaitAsync();
        try
        {
            if (_initialized)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            if (File.Exists(DatabasePath))
                await DataProtectionService.BackupAsync(DatabasePath,ApplicationDataPaths.BackupDirectory);

            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            await ExecuteAsync(connection, null, "PRAGMA journal_mode=WAL;");
            await ExecuteAsync(connection, null, "PRAGMA synchronous=NORMAL;");
            await ExecuteAsync(connection, null, "PRAGMA foreign_keys=ON;");
            await ExecuteAsync(connection, null, "PRAGMA busy_timeout=5000;");

            var schema = """
            CREATE TABLE IF NOT EXISTS AppState (
                StateKey TEXT PRIMARY KEY,
                Json TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Drivers (
                Id TEXT PRIMARY KEY,
                FirstName TEXT NOT NULL,
                LastName TEXT NOT NULL,
                Nickname TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                Json TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Classes (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                RaceFormat INTEGER NOT NULL DEFAULT 0,
                RaceMinutes REAL NOT NULL DEFAULT 5,
                TargetLaps INTEGER NOT NULL DEFAULT 50,
                MinimumLapSeconds REAL NOT NULL DEFAULT 1,
                MaxRacersPerHeat INTEGER NOT NULL DEFAULT 19,
                MaxRacersPerMain INTEGER NOT NULL DEFAULT 19,
                Json TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ClassMainRules (
                ClassId TEXT NOT NULL,
                RuleOrder INTEGER NOT NULL,
                MaxRacers INTEGER NOT NULL,
                BumpInsFromLower INTEGER NOT NULL,
                RunCount INTEGER NOT NULL DEFAULT 1,
                CountBest INTEGER NOT NULL DEFAULT 1,
                Json TEXT NOT NULL,
                PRIMARY KEY(ClassId, RuleOrder)
            );

            CREATE INDEX IF NOT EXISTS IX_ClassMainRules_Class
                ON ClassMainRules(ClassId);

            CREATE TABLE IF NOT EXISTS DriverClassAssignments (
                Id TEXT PRIMARY KEY,
                DriverId TEXT NOT NULL,
                ClassId TEXT NOT NULL,
                TransponderId TEXT NOT NULL,
                SeedRank INTEGER NOT NULL,
                Json TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_DriverClassAssignments_Driver
                ON DriverClassAssignments(DriverId);
            CREATE INDEX IF NOT EXISTS IX_DriverClassAssignments_Class
                ON DriverClassAssignments(ClassId);
            CREATE INDEX IF NOT EXISTS IX_DriverClassAssignments_Transponder
                ON DriverClassAssignments(TransponderId);

            CREATE TABLE IF NOT EXISTS Events (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                EventDate TEXT NOT NULL,
                Status INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                Json TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Events_Date
                ON Events(EventDate);
            CREATE INDEX IF NOT EXISTS IX_Events_Status
                ON Events(Status);

            CREATE TABLE IF NOT EXISTS EventClasses (
                EventId TEXT NOT NULL,
                ClassId TEXT NOT NULL,
                RunOrder INTEGER NOT NULL,
                PRIMARY KEY(EventId, ClassId)
            );

            CREATE TABLE IF NOT EXISTS EventDriverAssignments (
                Id TEXT PRIMARY KEY,
                EventId TEXT NOT NULL,
                DriverId TEXT NOT NULL,
                ClassId TEXT NOT NULL,
                TransponderId TEXT NOT NULL,
                SeedRank INTEGER NOT NULL,
                Json TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_EventDriverAssignments_Event
                ON EventDriverAssignments(EventId);
            CREATE INDEX IF NOT EXISTS IX_EventDriverAssignments_Driver
                ON EventDriverAssignments(DriverId);

            CREATE TABLE IF NOT EXISTS EventSessions (
                Id TEXT PRIMARY KEY,
                EventId TEXT NOT NULL,
                Name TEXT NOT NULL,
                ClassId TEXT NOT NULL,
                ClassName TEXT NOT NULL,
                SessionType INTEGER NOT NULL,
                RaceFormat INTEGER NOT NULL DEFAULT 0,
                TargetLaps INTEGER NOT NULL DEFAULT 50,
                Round INTEGER NOT NULL,
                Heat INTEGER NOT NULL,
                QueueOrder INTEGER NOT NULL,
                RaceNumber INTEGER NOT NULL DEFAULT 0,
                MainCapacity INTEGER NOT NULL DEFAULT 0,
                MainLeg INTEGER NOT NULL DEFAULT 1,
                MainRunCount INTEGER NOT NULL DEFAULT 1,
                MainCountBest INTEGER NOT NULL DEFAULT 1,
                MainSortOrder INTEGER NOT NULL DEFAULT 0,
                BumpInCount INTEGER NOT NULL DEFAULT 0,
                BumpUpCount INTEGER NOT NULL DEFAULT 0,
                BumpTargetSessionId TEXT NOT NULL DEFAULT '',
                ProgressStatus INTEGER NOT NULL,
                StartedAt TEXT NULL,
                CompletedAt TEXT NULL,
                Json TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_EventSessions_Event
                ON EventSessions(EventId);
            CREATE INDEX IF NOT EXISTS IX_EventSessions_Class
                ON EventSessions(ClassId);

            CREATE TABLE IF NOT EXISTS EventSessionEntrants (
                EventId TEXT NOT NULL,
                SessionId TEXT NOT NULL,
                CarNumber INTEGER NOT NULL,
                DriverId TEXT NOT NULL,
                DriverName TEXT NOT NULL,
                Nickname TEXT NOT NULL,
                TransponderId TEXT NOT NULL,
                SeedRank INTEGER NOT NULL,
                Json TEXT NOT NULL,
                PRIMARY KEY(SessionId, CarNumber)
            );

            CREATE TABLE IF NOT EXISTS EventSessionResults (
                EventId TEXT NOT NULL,
                SessionId TEXT NOT NULL,
                CarNumber INTEGER NOT NULL,
                Position INTEGER NOT NULL,
                DriverName TEXT NOT NULL,
                Json TEXT NOT NULL,
                PRIMARY KEY(SessionId, CarNumber)
            );

            CREATE TABLE IF NOT EXISTS EventSessionLaps (
                EventId TEXT NOT NULL,
                SessionId TEXT NOT NULL,
                Sequence INTEGER NOT NULL,
                CarNumber INTEGER NOT NULL,
                DriverName TEXT NOT NULL,
                LapNumber INTEGER NOT NULL,
                LapTimeSeconds REAL NOT NULL,
                ElapsedSeconds REAL NOT NULL,
                Timestamp TEXT NOT NULL,
                Source TEXT NOT NULL,
                Json TEXT NOT NULL,
                PRIMARY KEY(SessionId, Sequence)
            );

            CREATE INDEX IF NOT EXISTS IX_EventSessionLaps_SessionCar
                ON EventSessionLaps(SessionId, CarNumber, LapNumber);

            CREATE TABLE IF NOT EXISTS EventSessionBreakouts (
                EventId TEXT NOT NULL,
                SessionId TEXT NOT NULL,
                Sequence INTEGER NOT NULL,
                CarNumber INTEGER NOT NULL,
                DriverName TEXT NOT NULL,
                AttemptedLapNumber INTEGER NOT NULL,
                LapTimeSeconds REAL NOT NULL,
                MinimumLapSeconds REAL NOT NULL,
                Timestamp TEXT NOT NULL,
                Source TEXT NOT NULL,
                Json TEXT NOT NULL,
                PRIMARY KEY(SessionId, Sequence)
            );

            CREATE TABLE IF NOT EXISTS EventRankings (
                EventId TEXT NOT NULL,
                ClassId TEXT NOT NULL,
                DriverId TEXT NOT NULL,
                Rank INTEGER NOT NULL,
                DriverName TEXT NOT NULL,
                Json TEXT NOT NULL,
                PRIMARY KEY(EventId, ClassId, DriverId)
            );

            CREATE TABLE IF NOT EXISTS Series (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                StartDate TEXT NOT NULL,
                EndDate TEXT NOT NULL,
                PointsCsv TEXT NOT NULL,
                Json TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SeriesEvents (
                SeriesId TEXT NOT NULL,
                EventId TEXT NOT NULL,
                PRIMARY KEY(SeriesId, EventId)
            );

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
                Source TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_PracticeStints_StartedAt
                ON PracticeStints(StartedAt DESC);
            CREATE INDEX IF NOT EXISTS IX_PracticeLaps_StintId
                ON PracticeLaps(StintId, LapNumber);
            """;

            await ExecuteAsync(connection, null, schema);

            // v0.10 normalized columns. Existing v0.9.x databases keep all
            // data; missing columns are added in place. Full JSON snapshots
            // remain authoritative for forward/backward recovery.
            await EnsureColumnAsync(connection, "Classes", "RaceFormat", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(connection, "Classes", "RaceMinutes", "REAL NOT NULL DEFAULT 5");
            await EnsureColumnAsync(connection, "Classes", "TargetLaps", "INTEGER NOT NULL DEFAULT 50");
            await EnsureColumnAsync(connection, "Classes", "MinimumLapSeconds", "REAL NOT NULL DEFAULT 1");
            await EnsureColumnAsync(connection, "Classes", "MaxRacersPerHeat", "INTEGER NOT NULL DEFAULT 19");
            await EnsureColumnAsync(connection, "Classes", "MaxRacersPerMain", "INTEGER NOT NULL DEFAULT 19");

            await EnsureColumnAsync(connection, "EventSessions", "RaceNumber", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(connection, "EventSessions", "RaceFormat", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(connection, "EventSessions", "TargetLaps", "INTEGER NOT NULL DEFAULT 50");
            await EnsureColumnAsync(connection, "ClassMainRules", "RunCount", "INTEGER NOT NULL DEFAULT 1");
            await EnsureColumnAsync(connection, "ClassMainRules", "CountBest", "INTEGER NOT NULL DEFAULT 1");

            await EnsureColumnAsync(connection, "EventSessions", "MainCapacity", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(connection, "EventSessions", "MainLeg", "INTEGER NOT NULL DEFAULT 1");
            await EnsureColumnAsync(connection, "EventSessions", "MainRunCount", "INTEGER NOT NULL DEFAULT 1");
            await EnsureColumnAsync(connection, "EventSessions", "MainCountBest", "INTEGER NOT NULL DEFAULT 1");
            await EnsureColumnAsync(connection, "EventSessions", "MainSortOrder", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(connection, "EventSessions", "BumpInCount", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(connection, "EventSessions", "BumpUpCount", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(connection, "EventSessions", "BumpTargetSessionId", "TEXT NOT NULL DEFAULT ''");

            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    public async Task<AppSettings?> LoadApplicationStateAsync()
    {
        await InitializeAsync();

        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Json FROM AppState WHERE StateKey = 'Current' LIMIT 1;";

        var value = await command.ExecuteScalarAsync();
        if (value is not string json || string.IsNullOrWhiteSpace(json))
        {
            await using var existing = connection.CreateCommand();
            existing.CommandText="SELECT (SELECT COUNT(*) FROM Drivers)+(SELECT COUNT(*) FROM Events)+(SELECT COUNT(*) FROM Series);";
            if (Convert.ToInt64(await existing.ExecuteScalarAsync()) > 0)
                throw new InvalidDataException("The saved database contains race data but its application snapshot is missing. It has not been replaced with defaults.");
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? throw new InvalidDataException("The saved application snapshot is empty. It has not been replaced with defaults.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Stored application data could not be read. It has not been replaced with defaults.",ex);
        }
    }

    public async Task SaveApplicationStateAsync(AppSettings settings)
    {
        settings.RentalTransponders ??= new();
        IMRCRaceControl.Core.RentalTransponderRegistry.ValidateState(settings.RentalTransponders);
        IMRCRaceControl.Core.RentalTransponderRegistry.CaptureAssignments(settings);
        await InitializeAsync();

        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        // One complete snapshot is retained so startup/recovery is atomic.
        var stateJson = JsonSerializer.Serialize(settings, JsonOptions);
        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO AppState(StateKey, Json, UpdatedAt)
            VALUES('Current', $json, $updatedAt)
            ON CONFLICT(StateKey) DO UPDATE SET
                Json = excluded.Json,
                UpdatedAt = excluded.UpdatedAt;
            """,
            ("$json", stateJson),
            ("$updatedAt", DateTimeOffset.Now.ToString("O")));

        // The queryable entity tables are rebuilt inside the same transaction.
        // This keeps them exactly synchronized with the atomic AppState snapshot.
        foreach (var table in new[]
                 {
                     "Drivers",
                     "Classes",
                     "ClassMainRules",
                     "DriverClassAssignments",
                     "Events",
                     "EventClasses",
                     "EventDriverAssignments",
                     "EventSessions",
                     "EventSessionEntrants",
                     "EventSessionResults",
                     "EventSessionLaps",
                     "EventSessionBreakouts",
                     "EventRankings",
                     "SeriesEvents",
                     "Series"
                 })
        {
            await ExecuteAsync(connection, transaction, $"DELETE FROM {table};");
        }

        foreach (var driver in settings.Drivers)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO Drivers(Id, FirstName, LastName, Nickname, DisplayName, Json)
                VALUES($id, $first, $last, $nickname, $display, $json);
                """,
                ("$id", driver.Id),
                ("$first", driver.FirstName ?? string.Empty),
                ("$last", driver.LastName ?? string.Empty),
                ("$nickname", driver.Nickname ?? string.Empty),
                ("$display", driver.DisplayName),
                ("$json", JsonSerializer.Serialize(driver, JsonOptions)));
        }

        foreach (var cls in settings.Classes)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO Classes(
                    Id, Name, RaceFormat, RaceMinutes, TargetLaps, MinimumLapSeconds,
                    MaxRacersPerHeat, MaxRacersPerMain, Json)
                VALUES(
                    $id, $name, $raceFormat, $raceMinutes, $targetLaps, $minimumLap,
                    $maxHeat, $maxMain, $json);
                """,
                ("$id", cls.Id),
                ("$name", cls.Name),
                ("$raceFormat", (int)cls.RaceFormat),
                ("$raceMinutes", cls.RaceMinutes),
                ("$targetLaps", cls.TargetLaps),
                ("$minimumLap", cls.MinimumLapSeconds),
                ("$maxHeat", cls.MaxRacersPerHeat),
                ("$maxMain", cls.MaxRacersPerMain),
                ("$json", JsonSerializer.Serialize(cls, JsonOptions)));

            foreach (var rule in (cls.MainRules ?? []).OrderBy(r => r.Order))
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    INSERT INTO ClassMainRules(
                        ClassId, RuleOrder, MaxRacers, BumpInsFromLower, RunCount, CountBest, Json)
                    VALUES(
                        $classId, $order, $maxRacers, $bumpIns, $runCount, $countBest, $json);
                    """,
                    ("$classId", cls.Id),
                    ("$order", rule.Order),
                    ("$maxRacers", rule.MaxRacers),
                    ("$bumpIns", rule.BumpInsFromLower),
                    ("$runCount", rule.RunCount),
                    ("$countBest", rule.CountBest),
                    ("$json", JsonSerializer.Serialize(rule, JsonOptions)));
            }
        }

        foreach (var assignment in settings.DriverClassAssignments)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO DriverClassAssignments
                    (Id, DriverId, ClassId, TransponderId, SeedRank, Json)
                VALUES
                    ($id, $driverId, $classId, $transponder, $seed, $json);
                """,
                ("$id", assignment.Id),
                ("$driverId", assignment.DriverId),
                ("$classId", assignment.ClassId),
                ("$transponder", assignment.TransponderId ?? string.Empty),
                ("$seed", assignment.SeedRank),
                ("$json", JsonSerializer.Serialize(assignment, JsonOptions)));
        }

        foreach (var ev in settings.Events)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO Events
                    (Id, Name, EventDate, Status, CreatedAt, CompletedAt, Json)
                VALUES
                    ($id, $name, $date, $status, $created, $completed, $json);
                """,
                ("$id", ev.Id),
                ("$name", ev.Name),
                ("$date", ev.EventDate.ToString("yyyy-MM-dd")),
                ("$status", (int)ev.Status),
                ("$created", ev.CreatedAt.ToString("O")),
                ("$completed", ev.CompletedAt?.ToString("O")),
                ("$json", JsonSerializer.Serialize(ev, JsonOptions)));

            for (var classIndex = 0; classIndex < ev.ClassIds.Count; classIndex++)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    "INSERT INTO EventClasses(EventId, ClassId, RunOrder) VALUES($eventId, $classId, $runOrder);",
                    ("$eventId", ev.Id),
                    ("$classId", ev.ClassIds[classIndex]),
                    ("$runOrder", classIndex + 1));
            }

            foreach (var assignment in ev.DriverAssignments)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    INSERT INTO EventDriverAssignments
                        (Id, EventId, DriverId, ClassId, TransponderId, SeedRank, Json)
                    VALUES
                        ($id, $eventId, $driverId, $classId, $transponder, $seed, $json);
                    """,
                    ("$id", assignment.Id),
                    ("$eventId", ev.Id),
                    ("$driverId", assignment.DriverId),
                    ("$classId", assignment.ClassId),
                    ("$transponder", assignment.TransponderId ?? string.Empty),
                    ("$seed", assignment.SeedRank),
                    ("$json", JsonSerializer.Serialize(assignment, JsonOptions)));
            }

            foreach (var session in ev.Sessions)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    INSERT INTO EventSessions
                        (Id, EventId, Name, ClassId, ClassName, SessionType, RaceFormat, TargetLaps,
                         Round, Heat, QueueOrder, RaceNumber, MainCapacity, MainLeg, MainRunCount, MainCountBest, MainSortOrder,
                         BumpInCount, BumpUpCount, BumpTargetSessionId, ProgressStatus, StartedAt, CompletedAt, Json)
                    VALUES
                        ($id, $eventId, $name, $classId, $className, $sessionType, $raceFormat, $targetLaps,
                         $round, $heat, $queueOrder, $raceNumber, $mainCapacity, $mainLeg, $mainRunCount, $mainCountBest, $mainSortOrder,
                         $bumpIn, $bumpUp, $bumpTarget, $progressStatus, $startedAt, $completedAt, $json);
                    """,
                    ("$id", session.Id),
                    ("$eventId", ev.Id),
                    ("$name", session.Name),
                    ("$classId", session.ClassId),
                    ("$className", session.ClassName),
                    ("$sessionType", (int)session.SessionType),
                    ("$raceFormat", (int)session.RaceFormat),
                    ("$targetLaps", session.TargetLaps),
                    ("$round", session.Round),
                    ("$heat", session.Heat),
                    ("$queueOrder", session.QueueOrder),
                    ("$raceNumber", session.RaceNumber),
                    ("$mainCapacity", session.MainCapacity),
                    ("$mainLeg", session.MainLeg),
                    ("$mainRunCount", session.MainRunCount),
                    ("$mainCountBest", session.MainCountBest),
                    ("$mainSortOrder", (int)session.MainSortOrder),
                    ("$bumpIn", session.BumpInCount),
                    ("$bumpUp", session.BumpUpCount),
                    ("$bumpTarget", session.BumpTargetSessionId ?? string.Empty),
                    ("$progressStatus", (int)session.ProgressStatus),
                    ("$startedAt", session.StartedAt?.ToString("O")),
                    ("$completedAt", session.CompletedAt?.ToString("O")),
                    ("$json", JsonSerializer.Serialize(session, JsonOptions)));

                foreach (var entrant in session.Entrants)
                {
                    await ExecuteAsync(
                        connection,
                        transaction,
                        """
                        INSERT INTO EventSessionEntrants
                            (EventId, SessionId, CarNumber, DriverId, DriverName, Nickname,
                             TransponderId, SeedRank, Json)
                        VALUES
                            ($eventId, $sessionId, $car, $driverId, $driverName, $nickname,
                             $transponder, $seed, $json);
                        """,
                        ("$eventId", ev.Id),
                        ("$sessionId", session.Id),
                        ("$car", entrant.CarNumber),
                        ("$driverId", entrant.DriverId),
                        ("$driverName", entrant.DriverName),
                        ("$nickname", entrant.Nickname ?? string.Empty),
                        ("$transponder", entrant.TransponderId ?? string.Empty),
                        ("$seed", entrant.SeedRank),
                        ("$json", JsonSerializer.Serialize(entrant, JsonOptions)));
                }

                foreach (var result in session.Results)
                {
                    await ExecuteAsync(
                        connection,
                        transaction,
                        """
                        INSERT INTO EventSessionResults
                            (EventId, SessionId, CarNumber, Position, DriverName, Json)
                        VALUES
                            ($eventId, $sessionId, $car, $position, $driverName, $json);
                        """,
                        ("$eventId", ev.Id),
                        ("$sessionId", session.Id),
                        ("$car", result.CarNumber),
                        ("$position", result.Position),
                        ("$driverName", result.DriverName),
                        ("$json", JsonSerializer.Serialize(result, JsonOptions)));
                }

                foreach (var lap in session.Laps)
                {
                    await ExecuteAsync(
                        connection,
                        transaction,
                        """
                        INSERT INTO EventSessionLaps
                            (EventId, SessionId, Sequence, CarNumber, DriverName, LapNumber,
                             LapTimeSeconds, ElapsedSeconds, Timestamp, Source, Json)
                        VALUES
                            ($eventId, $sessionId, $sequence, $car, $driverName, $lapNumber,
                             $lapTime, $elapsed, $timestamp, $source, $json);
                        """,
                        ("$eventId", ev.Id),
                        ("$sessionId", session.Id),
                        ("$sequence", lap.Sequence),
                        ("$car", lap.CarNumber),
                        ("$driverName", lap.DriverName),
                        ("$lapNumber", lap.LapNumber),
                        ("$lapTime", lap.LapTime.TotalSeconds),
                        ("$elapsed", lap.Elapsed.TotalSeconds),
                        ("$timestamp", lap.Timestamp.ToString("O")),
                        ("$source", lap.Source),
                        ("$json", JsonSerializer.Serialize(lap, JsonOptions)));
                }

                foreach (var breakout in session.Breakouts)
                {
                    await ExecuteAsync(
                        connection,
                        transaction,
                        """
                        INSERT INTO EventSessionBreakouts
                            (EventId, SessionId, Sequence, CarNumber, DriverName, AttemptedLapNumber,
                             LapTimeSeconds, MinimumLapSeconds, Timestamp, Source, Json)
                        VALUES
                            ($eventId, $sessionId, $sequence, $car, $driverName, $attemptedLap,
                             $lapTime, $minimumLap, $timestamp, $source, $json);
                        """,
                        ("$eventId", ev.Id),
                        ("$sessionId", session.Id),
                        ("$sequence", breakout.Sequence),
                        ("$car", breakout.CarNumber),
                        ("$driverName", breakout.DriverName),
                        ("$attemptedLap", breakout.AttemptedLapNumber),
                        ("$lapTime", breakout.LapTime.TotalSeconds),
                        ("$minimumLap", breakout.MinimumLapTime.TotalSeconds),
                        ("$timestamp", breakout.Timestamp.ToString("O")),
                        ("$source", breakout.Source),
                        ("$json", JsonSerializer.Serialize(breakout, JsonOptions)));
                }
            }

            foreach (var ranking in ev.Rankings)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    INSERT INTO EventRankings
                        (EventId, ClassId, DriverId, Rank, DriverName, Json)
                    VALUES
                        ($eventId, $classId, $driverId, $rank, $driverName, $json);
                    """,
                    ("$eventId", ev.Id),
                    ("$classId", ranking.ClassId),
                    ("$driverId", ranking.DriverId),
                    ("$rank", ranking.Rank),
                    ("$driverName", ranking.DriverName),
                    ("$json", JsonSerializer.Serialize(ranking, JsonOptions)));
            }
        }

        foreach (var series in settings.Series)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO Series(Id, Name, StartDate, EndDate, PointsCsv, Json)
                VALUES($id, $name, $startDate, $endDate, $points, $json);
                """,
                ("$id", series.Id),
                ("$name", series.Name),
                ("$startDate", series.StartDate.ToString("yyyy-MM-dd")),
                ("$endDate", series.EndDate.ToString("yyyy-MM-dd")),
                ("$points", series.PointsCsv ?? string.Empty),
                ("$json", JsonSerializer.Serialize(series, JsonOptions)));

            foreach (var eventId in series.EventIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    "INSERT INTO SeriesEvents(SeriesId, EventId) VALUES($seriesId, $eventId);",
                    ("$seriesId", series.Id),
                    ("$eventId", eventId));
            }
        }

        transaction.Commit();
        try { await DataProtectionService.BackupAsync(DatabasePath,ApplicationDataPaths.BackupDirectory); }
        catch (Exception ex) { AppCrashLogger.Log("Automatic database backup",ex); }
    }

    public async Task<string> GetDatabaseSummaryAsync()
    {
        await InitializeAsync();

        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        var counts = new List<string>();
        foreach (var table in new[]
                 {
                     "Drivers", "Classes", "ClassMainRules", "DriverClassAssignments", "Events",
                     "EventSessions", "EventSessionLaps", "EventSessionBreakouts",
                     "Series", "PracticeStints", "PracticeLaps"
                 })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            var count = Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L);
            counts.Add($"{table}={count}");
        }

        return string.Join(", ", counts);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        string definition)
    {
        await using var query = connection.CreateCommand();
        query.CommandText = $"PRAGMA table_info({table});";

        await using var reader = await query.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(
                    reader.GetString(1),
                    column,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await reader.DisposeAsync();

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }
}
