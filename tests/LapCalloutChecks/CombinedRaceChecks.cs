using System.Text.Json;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
internal static class CombinedRaceChecks
{
    public static int Run()
    {
        int checks=0;
        void Check(bool ok,string message){if(!ok)throw new Exception("Combined race: "+message);checks++;}
        var classes=new[]{new ClassDefinition {Id="A",Name="Stock",RaceMinutes=4},new ClassDefinition {Id="B",Name="Modified",RaceMinutes=4}};
        var a=new EventSessionDefinition {Id="a",ClassId="A",ClassName="Stock",QueueOrder=4,RaceNumber=4,StartStyle=StartStyle.Tone,RaceFormat=RaceFormat.Laps,TargetLaps=2};
        var b=new EventSessionDefinition {Id="b",ClassId="B",ClassName="Modified",QueueOrder=7,RaceNumber=7,StartStyle=StartStyle.Tone,RaceFormat=RaceFormat.Laps,TargetLaps=2};
        foreach(var s in new[]{a,b})for(int i=1;i<=2;i++)s.Entrants.Add(new SessionEntrant {CarNumber=i,DriverId=s.Id+i,DriverName=s.ClassName+i,TransponderId=s.Id+i});
        CombinedRaces.Validate([a,b],classes);checks++;
        void Reject(Action change,Action undo,string message)
        {change();bool rejected=false;try{CombinedRaces.Validate([a,b],classes);}catch(InvalidOperationException){rejected=true;}undo();Check(rejected,message);}
        Reject(()=>b.Round=2,()=>b.Round=1,"different rounds rejected");
        Reject(()=>b.ProgressStatus=EventSessionStatus.Completed,()=>b.ProgressStatus=EventSessionStatus.Planned,"completed races locked");
        Reject(()=>b.StartStyle=StartStyle.IndividualCrossing,()=>b.StartStyle=StartStyle.Tone,"incompatible clocks rejected");
        Reject(()=>b.TargetLaps=3,()=>b.TargetLaps=2,"incompatible target rejected");
        Reject(()=>b.Entrants[0].TransponderId=" A1 ",()=>b.Entrants[0].TransponderId="b1","duplicate decoder identity rejected");
        Reject(()=>b.Entrants[0].DriverId="a1",()=>b.Entrants[0].DriverId="b1","driver cannot race twice");
        Reject(()=>a.BumpTargetSessionId="b",()=>a.BumpTargetSessionId="","bump destination cannot share source run");
        a.CombinedRaceId=b.CombinedRaceId="group";a.CombinedRaceOrder=1;b.CombinedRaceOrder=2;
        var run=CombinedRaces.Run([a,b],b);
        Check(CombinedRaces.TrackCar([a,b],b,1)==3,"printed B car matches runtime number");
        Reject(()=>b.Entrants[0].TransponderId="other;a1",()=>b.Entrants[0].TransponderId="b1","duplicate transponder alias rejected");
        Check(run.Id==a.Id && run.IsCombinedRace,"child selection loads same leader run");
        Check(run.Entrants.Select(e=>e.CarNumber).SequenceEqual(new[]{1,2,3,4}),"unique runtime cars");
        Check(a.Entrants.Select(e=>e.CarNumber).SequenceEqual(new[]{1,2}) && b.Entrants.Select(e=>e.CarNumber).SequenceEqual(new[]{1,2}),"original cars unchanged");
        Check(CombinedRaces.Runs([a,b]).Count==1,"queue has one start");
        Check(run.Name.Contains("Stock + Modified"),"both class names in run header");
        var start=DateTimeOffset.Parse("2026-11-08T12:00:00Z");
        var engine=new RaceEngine();
        var racers=run.Entrants.Select(e=>new RacerEntry {CarNumber=e.CarNumber,DriverId=e.DriverId,DriverName=e.DriverName,ScoringGroupId=e.SourceSessionId,
            ClassName=e.SourceSessionId=="a"?"Stock":"Modified",MinimumLapSeconds=e.SourceSessionId=="a"?1:2}).ToList();
        engine.Start(racers,TimeSpan.Zero,2,RaceFormat.Laps,TimeSpan.FromSeconds(1),SessionType.Race,StartStyle.Tone,start);
        engine.RegisterPassing(1,"decoder",start.AddSeconds(3));engine.RegisterPassing(2,"decoder",start.AddSeconds(4));
        engine.RegisterPassing(3,"decoder",start.AddSeconds(5));engine.RegisterPassing(1,"decoder",start.AddSeconds(6));
        engine.RegisterPassing(4,"decoder",start.AddSeconds(6.1));
        Check(engine.GetStandings(start.AddSeconds(6.1)).Single(r=>r.CarNumber==4).Status!="FINISHED","A leader target does not finish B driver");
        Check(engine.RegisterPassing(3,"decoder",start.AddSeconds(6.5)).Code==PassingResultCode.MinimumLapRejected,"per-class minimum enforced");
        engine.RegisterPassing(2,"decoder",start.AddSeconds(8));
        Check(engine.State!=RaceState.Complete,"A finished while B still racing");
        engine.RegisterPassing(3,"decoder",start.AddSeconds(10));engine.RegisterPassing(4,"decoder",start.AddSeconds(12.2));
        Check(engine.AllDriversFinished && engine.State==RaceState.Complete,"automatic complete waits for both fields");
        run.Results=engine.GetStandings(start.AddSeconds(13)).ToList();run.Laps=engine.Laps.ToList();run.Breakouts=engine.Breakouts.ToList();run.ProgressStatus=EventSessionStatus.Completed;
        foreach(var group in run.Results.GroupBy(r=>r.ScoringGroupId))
        {
            Check(group.Select(r=>r.Position).Order().SequenceEqual(new[]{1,2}),"positions independently restart at one");
            Check(group.Single(r=>r.Position==1).GapToLeader=="LEADER","independent leader gap");
            Check(group.Any(r=>r.IsFastestLap),"independent class fastest lap");
        }
        CombinedRaces.WriteBack(run);
        Check(a.ProgressStatus==EventSessionStatus.Completed && b.ProgressStatus==EventSessionStatus.Completed,"both saved completed");
        Check(a.Results.Count==2 && b.Results.Count==2 && a.Laps.Count==4 && b.Laps.Count==4,"results split to source heat");
        Check(b.Laps.All(l=>l.CarNumber is 1 or 2) && b.Results.All(r=>r.CarNumber is 1 or 2),"runtime numbers restored in histories");
        Check(b.Breakouts.Count==1 && a.Breakouts.Count==0,"breakout saved to correct class");
        var persisted=JsonSerializer.Deserialize<List<EventSessionDefinition>>(JsonSerializer.Serialize(new[]{a,b}))!;
        var recall=CombinedRaces.Runs(persisted).Single();
        Check(recall.Results.Count==4 && recall.Laps.Count==8 && recall.Entrants.Count==4,"combined recall survives database roundtrip");
        Check(persisted.All(s=>s.CombinedMembers.Count==0),"runtime facade never stored recursively");
        run.ProgressStatus=EventSessionStatus.Loaded;run.Results.Clear();run.Laps.Clear();run.Breakouts.Clear();CombinedRaces.WriteBack(run);
        Check(a.Results.Count==0 && b.Results.Count==0 && a.ProgressStatus==EventSessionStatus.Loaded && b.ProgressStatus==EventSessionStatus.Loaded,"restart clears entire combined run");
        // Leader crossing starts each separate class field, not just the first class.
        engine.Start(racers,TimeSpan.FromMinutes(4),2,RaceFormat.Timed,TimeSpan.FromSeconds(1),SessionType.Qualifying,StartStyle.LeaderCrossing,start);
        Check(engine.RegisterPassing(2,"decoder",start.AddSeconds(1)).Code==PassingResultCode.PreStartRejected,"class A waits for own leader");
        engine.RegisterPassing(1,"decoder",start.AddSeconds(2));
        Check(engine.RegisterPassing(4,"decoder",start.AddSeconds(3)).Code==PassingResultCode.PreStartRejected,"class B waits for own leader after A start");
        Check(engine.RegisterPassing(3,"decoder",start.AddSeconds(4)).Code==PassingResultCode.StartCrossing,"class B clock begins on its leader");
        var track=new TrackProfile();
        var ev=new RaceDayEventDefinition {Track=track,Sessions=[a,b]};
        var liveRows=run.Entrants.Select(e=>new StandingRow {CarNumber=e.CarNumber,Position=e.CarNumber<=2?e.CarNumber:e.CarNumber-2,Laps=e.CarNumber*10,DriverName=e.DriverName}).ToList();
        var capture=OnlineSnapshotBuilder.Capture(track,[ev],[],[],classes,ev.Id,run.Id,RaceState.Running,liveRows,[],true);
        var uploaded=capture.Documents.Single().Value.GetProperty("races");
        Check(uploaded[0].GetProperty("drivers")[0].GetProperty("laps").GetInt32()==10,"live upload retains A timing");
        Check(uploaded[1].GetProperty("drivers")[0].GetProperty("laps").GetInt32()==30,"live upload maps B timing independently");
        Check(uploaded[1].GetProperty("status").GetString()=="live","both classes upload live");
        a.ProgressStatus=b.ProgressStatus=EventSessionStatus.Skipped;
        CombinedRaces.WriteBack(run);
        Check(a.ProgressStatus==EventSessionStatus.Skipped && b.ProgressStatus==EventSessionStatus.Skipped,"ending event cannot revive an unrun combined race");
        a.ProgressStatus=b.ProgressStatus=EventSessionStatus.Planned;
        CombinedRaces.WriteBack(run);
        Check(run.ProgressStatus==EventSessionStatus.Planned && a.ProgressStatus==EventSessionStatus.Planned && b.ProgressStatus==EventSessionStatus.Planned,"reopening event preserves reactivated combined races");
        a.CombinedRaceId=b.CombinedRaceId="";
        Check(CombinedRaces.Runs([a,b]).Count==2,"separate restores two starts");
        return checks;
    }
}
