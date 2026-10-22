using System.Text.Json;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;
using Microsoft.Data.Sqlite;

public static class RaceScheduleChecks
{
    public static async Task<int> Run(bool isolated)
    {
        int checks=0;
        void Eq<T>(T expected,T actual,string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"{label}: expected {expected}, got {actual}");
            checks++;
        }
        EventSessionDefinition Qual(string cls,int round,int heat,int queue) => new() {
            ClassId=cls,ClassName=cls=="a" ? "Breakout" : "Stock",Round=round,Heat=heat,QueueOrder=queue,
            Name=$"{cls} Q{round} Heat {heat}",SessionType=SessionType.Qualifying };
        EventSessionDefinition Main(string cls,int leg,int heat,int queue) => new() {
            ClassId=cls,ClassName=cls=="a" ? "Breakout" : "Stock",Round=1,Heat=heat,MainLeg=leg,QueueOrder=queue,
            Name=$"{cls} Main {heat}/{leg}",SessionType=SessionType.Race };
        var q1a1=Qual("a",1,1,1);var q1a2=Qual("a",1,2,2);var q1b=Qual("b",1,1,3);
        var q2b=Qual("b",2,1,4);var q2a1=Qual("a",2,1,5);var q2a2=Qual("a",2,2,6);
        var m1b=Main("a",1,2,7);var m2b=Main("a",2,2,8);var m1a=Main("a",1,1,9);var m2a=Main("a",2,1,10);
        var ev=new RaceDayEventDefinition { Sessions=[q1a1,q1a2,q1b,q2b,q2a1,q2a2,m1b,m2b,m1a,m2a] };
        var queues=string.Join(',',ev.Sessions.Select(s=>s.QueueOrder));
        RaceSchedule.AssignRaceNumbers(ev.Sessions);
        Eq("1,2,3,3,1,2,1,1,2,2",string.Join(',',ev.Sessions.Select(s=>s.RaceNumber)),"Numbers repeat by class/heat even when later queue differs");
        Eq(queues,string.Join(',',ev.Sessions.Select(s=>s.QueueOrder)),"Numbering never changes event running order");
        Eq("Q1,Q2,M1,M2",string.Join(',',RaceSchedule.AvailableRounds(ev.Sessions).Select(r=>r.Label)),"Only created rounds appear in selector");
        Eq(0,RaceSchedule.AvailableRounds([]).Count,"Empty event has no rounds");
        Eq(3,RaceSchedule.InRound(ev.Sessions,new(SessionType.Qualifying,2)).Count,"Q2 filters only its races");
        Eq(2,RaceSchedule.InRound(ev.Sessions,new(SessionType.Race,2)).Count,"M2 uses main leg, not qualifying round field");
        Eq(m2b,RaceSchedule.InRound(ev.Sessions,new(SessionType.Race,2)).First(),"M2 keeps existing queue order");
        Eq("#1 • Breakout • Heat 1",q2a1.DashboardDisplay,"Race choice shows stable number, class and heat");
        Eq("#2 • Breakout • A-Main",m2a.DashboardDisplay,"Main choice avoids repeating selected round");
        q2b.ProgressStatus=EventSessionStatus.Completed;
        Eq(q2a1,RaceSchedule.InitialRace(RaceSchedule.InRound(ev.Sessions,new(SessionType.Qualifying,2))),"Round switch loads first remaining heat");
        q2a2.ProgressStatus=EventSessionStatus.Loaded;
        Eq(q2a2,RaceSchedule.InitialRace(RaceSchedule.InRound(ev.Sessions,new(SessionType.Qualifying,2))),"Already loaded race is retained");
        q2a1.ProgressStatus=q2a2.ProgressStatus=EventSessionStatus.Completed;
        Eq(q2b,RaceSchedule.InitialRace(RaceSchedule.InRound(ev.Sessions,new(SessionType.Qualifying,2))),"Completed round stays available for recall");
        var skipped=Qual("a",3,1,11);skipped.ProgressStatus=EventSessionStatus.Skipped;ev.Sessions.Add(skipped);
        Eq(false,RaceSchedule.AvailableRounds(ev.Sessions).Any(r=>r.Label=="Q3"),"Skipped-only round is not offered");
        Eq(0,RaceSchedule.InRound(ev.Sessions,new(SessionType.Qualifying,3)).Count,"Skipped races excluded");
        Eq<EventSessionDefinition?>(null,RaceSchedule.InitialRace([]),"Empty selection is safe");
        var previous=ev.Sessions.Where(s=>s!=skipped).ToDictionary(s=>s.Id,s=>s.RaceNumber);
        var extra=Qual("a",2,3,2);ev.Sessions.Add(extra);
        RaceSchedule.AssignRaceNumbers(ev.Sessions);
        Eq(4,extra.RaceNumber,"Added heat gets new number, never shifts later class");
        Eq(true,previous.All(p=>ev.Sessions.Single(s=>s.Id==p.Key).RaceNumber==p.Value),"All existing numbers preserved after addition");
        ev.Sessions.Remove(q1a2);
        q1b.QueueOrder=1;q1a1.QueueOrder=3;
        RaceSchedule.AssignRaceNumbers(ev.Sessions);
        Eq(3,q1b.RaceNumber,"Queue reorder keeps race number");
        Eq(2,q2a2.RaceNumber,"Removed earlier-round heat does not renumber later round");
        var q4=Qual("a",4,3,12);ev.Sessions.Add(q4);RaceSchedule.AssignRaceNumbers(ev.Sessions);
        Eq(extra.RaceNumber,q4.RaceNumber,"Future round adopts late heat number");
        var numberSnapshot=string.Join(',',ev.Sessions.Select(s=>s.RaceNumber));
        RaceSchedule.AssignRaceNumbers(ev.Sessions.AsEnumerable().Reverse());
        Eq(numberSnapshot,string.Join(',',ev.Sessions.Select(s=>s.RaceNumber)),"Saved numbering survives collection reorder");
        var rebuild=Qual("a",1,1,1);
        SavedRoundEditor.ReplaceScope(ev,[q1a1],[rebuild]);
        Eq(q1a1.RaceNumber,rebuild.RaceNumber,"Rebuilt saved heat retains number despite new ID");
        var legacy=JsonSerializer.Deserialize<RaceDayEventDefinition>("{\"Sessions\":[{\"ClassId\":\"a\",\"QueueOrder\":1},{\"ClassId\":\"a\",\"Round\":2,\"QueueOrder\":8}]}")!;
        RaceSchedule.AssignRaceNumbers(legacy.Sessions);
        Eq("1,1",string.Join(',',legacy.Sessions.Select(s=>s.RaceNumber)),"Legacy event without numbers migrates by round");
        var loaded=JsonSerializer.Deserialize<RaceDayEventDefinition>(JsonSerializer.Serialize(ev))!;
        Eq(string.Join(',',ev.Sessions.Select(s=>s.RaceNumber)),string.Join(',',loaded.Sessions.Select(s=>s.RaceNumber)),"Numbers survive JSON reload");
        var print=RaceReports.Lineups(ev,"a",new(SessionType.Qualifying,2));
        Eq(true,print.Sections.Any(s=>s.RoundHeading=="Qualifying Round 2" && s.Title.StartsWith($"RACE #{q2a1.RaceNumber} -")),"Printed lineup matches dashboard round and number");
        Eq(true,ev.Sessions.GroupBy(DashboardRoundChoice.From).All(g=>g.Select(s=>s.RaceNumber).Distinct().Count()==g.Count()),"Every race number is unique inside a round");
        if (isolated)
        {
            var db=new RaceControlDatabaseService();
            if (!db.DatabasePath.StartsWith("/tmp/imrc-callout-test-data/")) throw new Exception("Non-isolated database");
            await db.SaveApplicationStateAsync(new AppSettings { Events=[ev] });
            var state=(await new RaceControlDatabaseService().LoadApplicationStateAsync())!;
            Eq(string.Join(',',ev.Sessions.Select(s=>s.RaceNumber)),string.Join(',',state.Events.Single().Sessions.Select(s=>s.RaceNumber)),"SQLite reopen retains every race number");
            await using var connection=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=db.DatabasePath }.ToString());
            await connection.OpenAsync();
            await using var command=connection.CreateCommand();
            command.CommandText="SELECT RaceNumber FROM EventSessions WHERE Id=$id";command.Parameters.AddWithValue("$id",q2a1.Id);
            Eq((long)q2a1.RaceNumber,(long)(await command.ExecuteScalarAsync())!,"Queryable SQLite number matches snapshot");
            // Exercise the real additive migration from the pre-0.13.4 table.
            command.Parameters.Clear();command.CommandText="ALTER TABLE EventSessions DROP COLUMN RaceNumber";await command.ExecuteNonQueryAsync();
            var migratedDb=new RaceControlDatabaseService();await migratedDb.InitializeAsync();
            var migrated=(await migratedDb.LoadApplicationStateAsync())!;
            Eq(state.Events[0].Sessions.Count,migrated.Events[0].Sessions.Count,"Database upgrade retains saved sessions");
            await migratedDb.SaveApplicationStateAsync(migrated);
            command.CommandText="SELECT RaceNumber FROM EventSessions WHERE Id=$id";command.Parameters.AddWithValue("$id",q2a1.Id);
            Eq((long)q2a1.RaceNumber,(long)(await command.ExecuteScalarAsync())!,"Migrated database persists number on next save");
        }
        return checks;
    }
}
