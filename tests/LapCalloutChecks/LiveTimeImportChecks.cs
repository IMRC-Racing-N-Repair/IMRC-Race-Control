using System.Security.Cryptography;
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
            Check(clean.Classes.Single().MinimumLapSeconds==3.5 && !again.HasChanges,"class minimum lap time and repeat import are stable");
            var sourceRental=LiveTimeRosterMerge.Preview(decoded with { RentalNumbers=["3456789"] },[],[],[]);
            Check(sourceRental.Assignments.Any(a=>a.TransponderId=="") && sourceRental.Issues.Count==1,"does not convert source rentals to personal tags");
            var sameName=LiveTimeRosterMerge.Preview(decoded,[existingDriver],[],[]);
            Check(sameName.Drivers.Count==1 && sameName.Issues.Count==1,"does not match on name alone");
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
        command.CommandText="""
            PRAGMA page_size=1024;
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
        command.ExecuteNonQuery();
        if(freePages)
        {
            command.CommandText="PRAGMA secure_delete=ON; CREATE TABLE Spare(Data BLOB); INSERT INTO Spare VALUES (zeroblob(30000)); DROP TABLE Spare;";
            command.ExecuteNonQuery();
        }
    }
}
