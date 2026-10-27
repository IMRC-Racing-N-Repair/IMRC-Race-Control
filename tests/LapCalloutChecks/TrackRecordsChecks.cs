using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

internal static class TrackRecordsChecks
{
    public static int Run()
    {
        int checks=0;
        void Check(bool value,string name) { if(!value) throw new Exception("Track records: "+name); checks++; }
        var track=new TrackProfile { Id="home",Name="Home" };
        var classes=new List<ClassDefinition> { new() { Id="a",Name="Stock" },new() { Id="b",Name="Modified" },new() { Id="empty",Name="Empty" } };
        var ev=new RaceDayEventDefinition { Track=track,EventDate=new DateTime(2026,10,10),Name="Week 1" };
        EventSessionDefinition Session(string id)
        {
            var s=new EventSessionDefinition { ClassId=id,ClassName=id,ProgressStatus=EventSessionStatus.Completed };
            for(int i=1;i<=12;i++)
            {
                s.Entrants.Add(new() { CarNumber=i,DriverId="driver"+i,DriverName="Driver "+i });
                s.Laps.Add(new(i,i,"Driver "+i,1,TimeSpan.FromSeconds(5+i/10d),TimeSpan.FromSeconds(5+i/10d),DateTimeOffset.UtcNow,"decoder",false));
            }
            return s;
        }
        var a=Session("a"); ev.Sessions.Add(a); ev.Sessions.Add(Session("b"));
        // Same driver, another faster lap: only their best time belongs in the ranking.
        a.Laps.Add(a.Laps[0] with { LapNumber=2,LapTime=TimeSpan.FromSeconds(4.5) });
        a.Laps.Add(a.Laps[0] with { LapTime=TimeSpan.Zero });
        a.Breakouts.Add(new(99,1,"Driver 1",3,TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(3),DateTimeOffset.UtcNow,"decoder"));
        var foreign=new RaceDayEventDefinition { Track=new() { Id="other" },Sessions=[Session("a")] };
        foreign.Sessions[0].Laps=[a.Laps[0] with { LapTime=TimeSpan.FromSeconds(1) }];
        var unknown=new RaceDayEventDefinition { Sessions=[foreign.Sessions[0]] };
        var running=Session("a"); running.ProgressStatus=EventSessionStatus.Running; running.Laps=foreign.Sessions[0].Laps; ev.Sessions.Add(running);
        var practice=Session("a"); practice.SessionType=SessionType.Practice; practice.Laps=foreign.Sessions[0].Laps; ev.Sessions.Add(practice);
        var events=new[]{ev,foreign,unknown};
        var all=TrackRecords.Calculate(track,events,classes);
        Check(all.Count==3,"all classes including empty");
        var stock=all.Single(c=>c.Id=="a");
        Check(stock.Records.Count==10,"default ten unique drivers");
        Check(stock.Records[0].Time==TimeSpan.FromSeconds(4.5),"fastest valid lap excluding other tracks, practice, running, breakout and zero");
        Check(stock.Records[0].Date==ev.EventDate,"event date retained");
        Check(stock.Records[1].Driver=="Driver 2","duplicate driver excluded");
        Check(stock.Records.Select(r=>r.Rank).SequenceEqual(Enumerable.Range(1,10)),"rank ordering");
        var five=TrackRecords.Calculate(track,events,classes,5);
        Check(five.Where(c=>c.Id!="empty").All(c=>c.Records.Count==5),"five per class");
        var single=TrackRecords.Calculate(track,events,classes,5,"b");
        Check(single.Count==1 && single[0].Records.Count==5,"single class selection");
        var report=TrackRecords.Report(track.Name,five,5);
        Check(report.Sections.Count==3 && report.Sections.Where(s=>s.Title!="Empty").All(s=>s.Rows.Count==5),"print matches display");
        Check(report.Sections.Single(s=>s.Title=="Stock").Rows[0].SequenceEqual(new[]{"1","Driver 1","4.500","10/10/2026","Week 1"}),"print name time date and event");
        Check(TrackRecords.Calculate(track,events,classes,50).Single(c=>c.Id=="a").Records.Count==12,"count larger than available");
        Check(TrackRecords.Calculate(track,events,[]).Any(c=>c.Id=="a"),"historical class retained");
        bool rejected=false; try { TrackRecords.Calculate(track,events,classes,0); } catch(ArgumentOutOfRangeException) { rejected=true; }
        Check(rejected,"invalid count");
        return checks;
    }
}
