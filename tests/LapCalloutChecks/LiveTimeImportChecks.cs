using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;
using Microsoft.Data.Sqlite;

internal static class LiveTimeImportChecks
{
    internal static int Run()
    {
        var folder=Path.Combine(Path.GetTempPath(),"imrc-livetime-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var checks=0;
        void Check(bool condition,string what)
        {
            if(!condition)throw new Exception("LiveTime import: "+what);
            checks++;
        }
        try
        {
            var fixture=Path.Combine(folder,"source.db");
            var reference=Path.Combine(folder,"reference.db");
            var mask=RandomNumberGenerator.GetBytes(1024);
            CreateFixture(fixture,false);
            CreateFixture(reference,true);
            var original=File.ReadAllBytes(fixture);
            var plain=LiveTimeBackupReader.Read(fixture);
            Check(plain.SchemaVersion==193 && plain.Drivers.Count==2 && plain.Classes.Count==1,"recognizes active roster");
            Check(plain.Entries.First(e=>e.DriverId==1).Transponders=="1234567;2345678","keeps primary and additional transponders");
            Check(plain.Events==1 && plain.Laps==2 && plain.PracticeLaps==1,"reports history without importing it");
            Check(original.SequenceEqual(File.ReadAllBytes(fixture)),"plain database remains unchanged");

            var encrypted = Path.Combine(folder, "encrypted # fixture.db");
            var password = "sample import password"u8.ToArray();
            CreateSqlCipherFixture(encrypted, password);
            var encryptedBytes = File.ReadAllBytes(encrypted);
            var needsPassword = false;
            try { LiveTimeBackupReader.ReadImportRoster(encrypted); }
            catch (LiveTimeBackupPasswordRequiredException) { needsPassword = true; }
            Check(needsPassword, "encrypted .db prompts for its key");
            var plainRoster = LiveTimeBackupReader.ReadImportRoster(fixture);
            var encryptedRoster = LiveTimeBackupReader.ReadEncryptedRoster(encrypted, password);
            Check(plainRoster.Drivers.SequenceEqual(encryptedRoster.Drivers) &&
                plainRoster.Classes.SequenceEqual(encryptedRoster.Classes) &&
                plainRoster.Entries.SequenceEqual(encryptedRoster.Entries) &&
                plainRoster.Events == 0 && encryptedRoster.Laps == 0,
                "both import paths supply the same roster without reading race history");
            var countsRoster = encryptedRoster with
            {
                Entries = [.. encryptedRoster.Entries, new(1, 2, "01234567")],
                RentalNumbers = ["03456789"]
            };
            Check(LiveTimeRosterMerge.CountPersonalTransponders(countsRoster) == 2,
                "category count deduplicates personal transponders and excludes known rentals");
            var unlocked = LiveTimeBackupReader.ReadEncryptedImport(encrypted, password);
            Check(unlocked.Roster.Drivers.SequenceEqual(plain.Drivers) && unlocked.Roster.Entries.SequenceEqual(plain.Entries),
                "original encrypted .db opens directly with its password");
            var salt = encryptedBytes.AsSpan(0, 16);
            var raw = Rfc2898DeriveBytes.Pbkdf2(password, salt, 256000, HashAlgorithmName.SHA512, 32);
            var scopedKey = Encoding.ASCII.GetBytes("x'" + Convert.ToHexString(raw) + Convert.ToHexString(salt) + "'");
            Check(LiveTimeBackupReader.ReadEncryptedImport(encrypted, scopedKey).Roster.Entries.SequenceEqual(plain.Entries),
                "database-specific raw key opens the same encrypted backup");
            var wrongPassword = false;
            try { LiveTimeBackupReader.ReadEncryptedImport(encrypted, "incorrect"u8.ToArray()); }
            catch (InvalidDataException) { wrongPassword = true; }
            Check(wrongPassword, "wrong password rejects the backup");
            var tampered = Path.Combine(folder, "tampered.db");
            var altered = encryptedBytes.ToArray();
            altered[altered.Length - 1000] ^= 0xff;
            File.WriteAllBytes(tampered, altered);
            var badPage = false;
            try { LiveTimeBackupReader.ReadEncryptedImport(tampered, password); }
            catch (InvalidDataException) { badPage = true; }
            Check(badPage, "page authentication rejects a corrupted encrypted backup");
            Check(encryptedBytes.SequenceEqual(File.ReadAllBytes(encrypted)) && !File.Exists(encrypted+"-wal") &&
                !File.Exists(encrypted+"-shm") && !File.Exists(encrypted+"-journal"), "opening a backup never changes it or creates sidecars");

            var codedPath=Path.Combine(folder,"coded.db");
            var codedRef=Path.Combine(folder,"coded-reference.db");
            WriteLegacy(fixture,codedPath,mask);
            WriteLegacy(reference,codedRef,mask);
            var encoded=File.ReadAllBytes(codedPath);
            var decoded=LiveTimeBackupReader.Read(codedPath,codedRef);
            Check(decoded.Drivers.SequenceEqual(plain.Drivers) && decoded.Entries.SequenceEqual(plain.Entries),"reference opens a populated legacy backup");
            Check(encoded.SequenceEqual(File.ReadAllBytes(codedPath)),"legacy database remains unchanged");
            var padded=LiveTimeBackupReader.Read(codedRef);
            Check(padded.Classes.SequenceEqual(plain.Classes),"legacy backup with unused pages opens without reference");

            var wrong=Path.Combine(folder,"wrong.db");
            WriteLegacy(reference,wrong,RandomNumberGenerator.GetBytes(1024));
            var corrupt=Path.Combine(folder,"corrupt.db");
            var bad=encoded.ToArray();
            bad[100]^=255;
            File.WriteAllBytes(corrupt,bad);
            var rejected=false;
            try{LiveTimeBackupReader.Read(corrupt,codedRef);}
            catch(InvalidDataException){rejected=true;}
            Check(rejected,"corrupt database is rejected");
            rejected=false;
            try{LiveTimeBackupReader.Read(corrupt,wrong);}
            catch(InvalidDataException){rejected=true;}
            Check(rejected,"unmatched reference cannot accept damaged data");

            var existingDriver=new DriverProfile {FirstName="Casey",LastName="Lane"};
            var existingClass=new ClassDefinition {Name="Stock",RaceMinutes=7};
            var existingEntry=new DriverClassAssignment
            {
                DriverId=existingDriver.Id,ClassId=existingClass.Id,TransponderId="1234567"
            };
            var plan=LiveTimeRosterMerge.Preview(decoded,[existingDriver],[existingClass],[existingEntry],["3456789"]);
            Check(plan.MatchedDrivers==1 && plan.MatchedClasses==1 && plan.Drivers.Count==1,"matches known racer and class without replacing them");
            Check(existingEntry.TransponderId=="1234567" && existingClass.RaceMinutes==7,"preserves existing settings and assignment");
            Check(plan.Assignments.Single().TransponderId=="" && plan.Issues.Count==2,"flags the conflict and skips rental transponder");
            var clean=LiveTimeRosterMerge.Preview(decoded,[],[],[]);
            var again=LiveTimeRosterMerge.Preview(decoded,clean.Drivers,clean.Classes,clean.Assignments);
            var driversOnly = LiveTimeRosterMerge.Preview(decoded, [], [], [], selection: LiveTimeRosterSelection.Drivers);
            Check(driversOnly.Drivers.Count == 2 && driversOnly.Classes.Count == 0 && driversOnly.Assignments.Count == 0,
                "drivers-only selection cannot add classes or assignments");
            var classesOnly = LiveTimeRosterMerge.Preview(decoded, [], [], [], selection: LiveTimeRosterSelection.Classes);
            Check(classesOnly.Classes.Count == 1 && classesOnly.Drivers.Count == 0 && classesOnly.Assignments.Count == 0,
                "classes-only selection cannot add drivers or assignments");
            var membershipsOnly = LiveTimeRosterMerge.Preview(decoded, clean.Drivers, clean.Classes, [],
                selection: LiveTimeRosterSelection.ClassAssignments);
            Check(membershipsOnly.Assignments.Count == 2 && membershipsOnly.Assignments.All(a => a.TransponderId == ""),
                "class memberships can be imported without tag numbers");
            var missing = LiveTimeRosterMerge.Preview(decoded, [], [], [],
                selection: LiveTimeRosterSelection.ClassAssignments | LiveTimeRosterSelection.Transponders);
            Check(!missing.HasChanges && missing.SkippedMissingAssignments == 2,
                "assignment selection cannot silently create unselected drivers or classes");
            var tagsOnly = LiveTimeRosterMerge.Preview(decoded, clean.Drivers, clean.Classes, membershipsOnly.Assignments,
                selection: LiveTimeRosterSelection.Transponders);
            Check(tagsOnly.Assignments.Count == 0 && tagsOnly.TagFills.Count == 2 &&
                membershipsOnly.Assignments.All(a => a.TransponderId == ""),
                "tags-only preview fills blank assignments without creating or mutating records");
            var tagsMissingAssignment = LiveTimeRosterMerge.Preview(decoded, clean.Drivers, clean.Classes, [],
                selection: LiveTimeRosterSelection.Transponders);
            Check(!tagsMissingAssignment.HasChanges && tagsMissingAssignment.SkippedMissingAssignments == 2,
                "tags-only selection does not create missing class memberships");
            var tagsAlreadyAssigned = LiveTimeRosterMerge.Preview(decoded, [existingDriver], [existingClass], [existingEntry],
                selection: LiveTimeRosterSelection.Transponders);
            Check(!tagsAlreadyAssigned.HasChanges && existingEntry.TransponderId == "1234567",
                "tags-only selection preserves existing personal transponders");
            Check(!LiveTimeRosterMerge.Preview(decoded, [], [], [], selection: LiveTimeRosterSelection.None).HasChanges,
                "no selected category results in no writes");
            Check(clean.Classes.Single().MinimumLapSeconds==3.5 && !again.HasChanges,"class minimum lap time and repeat import are stable");
            var sourceRental=LiveTimeRosterMerge.Preview(decoded with { RentalNumbers=["3456789"] },[],[],[]);
            Check(sourceRental.Assignments.Any(a=>a.TransponderId=="") && sourceRental.Issues.Count==1,"does not convert source rentals to personal tags");
            var sameName=LiveTimeRosterMerge.Preview(decoded,[existingDriver],[],[]);
            Check(sameName.Drivers.Count==1 && sameName.Issues.Count==1,"does not match on name alone");
            var raceAt = DateTimeOffset.Parse("2025-08-03T12:00:00Z");
            var sourceEvent = new LiveTimeEventHistory(1, Guid.NewGuid(), "Event A", raceAt, raceAt.AddHours(4),
                [new(1, Guid.NewGuid(), 1, "Main", 2, 1, 2, 1, 1, "A", 1, 1, 2, 1, 300, 0, 3.5, raceAt, raceAt.AddMinutes(5),
                    [new(1, 1, 1, 2, 1, 5.5, 5.5, 5.5, 0, 0, 0, false, true, false, "1234567",
                        [new(1, 1, 5.5, 5.5, raceAt.AddSeconds(5.5))])])]);
            var otherEvent = sourceEvent with { SourceId = Guid.NewGuid(), Name = "Event B" };
            var archive = new LiveTimeImportSource(decoded, [sourceEvent, otherEvent]);
            var justOne = LiveTimeHistoryMerge.Preview(archive, [sourceEvent.SourceId], [], clean, includeLaps:true);
            var race = justOne.Single().Sessions.Single();
            Check(justOne[0].Name=="Event A" && race.ProgressStatus==EventSessionStatus.Completed &&
                race.Results.Single().Position==2 && race.Results[0].Status=="DNF" && race.Laps.Single().LapTime.TotalSeconds==5.5,
                "selected completed event retains source position, status and lap time");
            var summaries = LiveTimeHistoryMerge.Preview(archive, [otherEvent.SourceId], [], clean, includeLaps:false);
            Check(summaries.Single().Sessions.Single().Results.Count==1 && summaries[0].Sessions[0].Laps.Count==0,
                "results-only history selection excludes individual laps");
            Check(LiveTimeHistoryMerge.Preview(archive, [sourceEvent.SourceId], justOne, clean, includeLaps:true).Count==0,
                "events already imported are not overwritten or duplicated");
            return checks;
        }
        finally {Directory.Delete(folder,true);}
    }

    private static void WriteLegacy(string input,string output,byte[] mask)
    {
        var bytes=File.ReadAllBytes(input);
        for(var i=0;i<bytes.Length;i++)bytes[i]^=mask[i%mask.Length];
        File.WriteAllBytes(output,bytes);
    }

    private static void CreateFixture(string file,bool freePages)
    {
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Pooling=false}.ToString());
        db.Open();
        using var command=db.CreateCommand();
        command.CommandText="PRAGMA page_size=1024;"+FixtureSql;
        command.ExecuteNonQuery();
        if(freePages)
        {
            command.CommandText="PRAGMA secure_delete=ON; CREATE TABLE Spare(Data BLOB); INSERT INTO Spare VALUES (zeroblob(30000)); DROP TABLE Spare;";
            command.ExecuteNonQuery();
        }
    }

    private static void CreateSqlCipherFixture(string path, byte[] password)
    {
        var result = NativeOpen(path, out var db, 6, IntPtr.Zero);
        try
        {
            var cipher = NativeIndex("sqlcipher");
            if (result != 0 || cipher <= 0 || NativeConfig(db, "cipher", cipher) != cipher ||
                NativeConfigMode(db, "sqlcipher", "legacy", 4) != 4 ||
                NativeKey(db, password, password.Length) != 0 ||
                NativeExec(db, FixtureSql, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != 0)
                throw new InvalidOperationException("Could not create the synthetic SQLCipher backup.");
        }
        finally { if (db != IntPtr.Zero) NativeClose(db); }
    }

    private const string CipherLib = "sqlite3mc";
    [DllImport(CipherLib, EntryPoint = "sqlite3_open_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(CipherLib, EntryPoint = "sqlite3_close_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeClose(IntPtr db);
    [DllImport(CipherLib, EntryPoint = "sqlite3mc_cipher_index", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeIndex([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(CipherLib, EntryPoint = "sqlite3mc_config", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeConfig(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int value);
    [DllImport(CipherLib, EntryPoint = "sqlite3mc_config_cipher", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeConfigMode(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string cipher,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int value);
    [DllImport(CipherLib, EntryPoint = "sqlite3_key", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeKey(IntPtr db, byte[] key, int length);
    [DllImport(CipherLib, EntryPoint = "sqlite3_exec", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeExec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr arg, IntPtr error);

    private const string FixtureSql = """
            CREATE TABLE SystemVersion ([Database] INTEGER);
            INSERT INTO SystemVersion VALUES (193);
            CREATE TABLE Driver (LID INTEGER PRIMARY KEY, GUID TEXT, IsActive INTEGER, FirstName TEXT, LastName TEXT, NickName TEXT, PhoneticName TEXT);
            INSERT INTO Driver VALUES (1,'dbdceaf3-8d74-4c3b-a8c7-b72d3bbc4df7',1,'Casey','Lane','Casey','Kay-see');
            INSERT INTO Driver VALUES (2,'838bdf0b-eb08-424c-83bd-cf17ac27706b',1,'Riley','Reed',NULL,'');
            INSERT INTO Driver VALUES (3,'8cb6365e-e1e8-4cff-82f1-38c4c98e466f',0,'Old','Driver',NULL,'');
            CREATE TABLE RaceClass (LID INTEGER PRIMARY KEY, GUID TEXT, IsActive INTEGER, Name TEXT, Color TEXT);
            INSERT INTO RaceClass VALUES (1,'89156fa2-e91b-4cf9-b9e9-04e873d8f996',1,'Stock','#FF00BFAE');
            CREATE TABLE RaceClassDefaultSetting (LID INTEGER PRIMARY KEY, RaceClassLID INTEGER, MinimumLapTimeSeconds REAL);
            INSERT INTO RaceClassDefaultSetting VALUES (1,1,3.5);
            CREATE TABLE DriverRaceClassTransponder (LID INTEGER PRIMARY KEY, DriverLID INTEGER, RaceClassLID INTEGER, IsPrimary INTEGER, TransponderNumber INTEGER);
            INSERT INTO DriverRaceClassTransponder VALUES (1,1,1,0,2345678),(2,1,1,1,1234567),(3,2,1,1,3456789);
            CREATE TABLE Event (LID INTEGER PRIMARY KEY);
            INSERT INTO Event VALUES (1);
            CREATE TABLE Race (LID INTEGER PRIMARY KEY);
            INSERT INTO Race VALUES (1);
            CREATE TABLE RaceLap (LID INTEGER PRIMARY KEY);
            INSERT INTO RaceLap VALUES (1),(2);
            CREATE TABLE PracticeLap (LID INTEGER PRIMARY KEY);
            INSERT INTO PracticeLap VALUES (1);
            """;

}
