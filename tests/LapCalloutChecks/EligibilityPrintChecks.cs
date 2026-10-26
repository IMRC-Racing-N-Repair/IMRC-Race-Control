using System.Text.Json;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
public static class EligibilityPrintChecks
{
    public static int Run()
    {
        int checks=0;
        void Eq<T>(T expected,T actual,string name) { if (!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"{name}: expected {expected}, got {actual}"); checks++; }
        var date=new DateTime(2026,10,3);
        var drivers=Enumerable.Range(1,100).Select(i=>new DriverProfile { Id=$"d{i}",FirstName=$"Racer {i}" }).ToList();
        var planned=new EventSessionDefinition { ClassId="c1",ClassName="Breakout",SessionType=SessionType.Qualifying,Round=1,Name="Q1 Heat 1",QueueOrder=1,
            Entrants=drivers.Select((d,i)=>new SessionEntrant { DriverId=d.Id,DriverName=d.DisplayName,CarNumber=i+1 }).ToList() };
        var ev=new RaceDayEventDefinition { Name="Opening night",EventDate=date,ClassIds=["c1","c2"],Sessions=[planned],
            DriverAssignments=drivers.Select((d,i)=>new EventDriverAssignment { DriverId=d.Id,ClassId=i<10 ? "c1" : "c2" }).ToList() };
        var series=new RaceSeriesDefinition { StartDate=date,EndDate=date.AddDays(14),EventIds=[ev.Id],PointsSettings=new() };
        var rows=SeriesScoring.Calculate(series,[ev],"c1",drivers);
        Eq(10,rows.Count,"100 directory and stale lineup entries yield only 10 class signups");
        Eq(true,rows.All(r=>int.Parse(r.DriverId[1..])<=10),"Other-class signups excluded");
        Eq(true,rows.All(r=>r.Points==0),"Signed-up pending drivers retained with zero points");
        Eq(90,SeriesScoring.Calculate(series,[ev],"c2",drivers).Count,"Signup-only class works without built heats");
        Eq("Racer 11",SeriesScoring.Calculate(series,[ev],"c2",drivers).Single(r=>r.DriverId=="d11").DriverName,"Directory resolves signup-only name");
        series.Adjustments.Add(new SeriesPointsAdjustment { DriverId="stranger",DriverName="Unrelated driver",ClassId="c1",Amount=100,Note="Old adjustment" });
        series.Adjustments.Add(new SeriesPointsAdjustment { DriverId="d1",DriverName="Racer 1",ClassId="c1",Amount=-5,Note="Penalty" });
        Eq(10,SeriesScoring.Calculate(series,[ev],"c1",drivers).Count,"Adjustment alone cannot add driver");
        Eq(-5m,SeriesScoring.Calculate(series,[ev],"c1",drivers).Single(r=>r.DriverId=="d1").Points,"Eligible racer adjustment retained");
        ev.DriverAssignments.RemoveAll(a=>a.DriverId=="d1");
        planned.Results.Add(new StandingRow { CarNumber=1,DriverName="Racer 1",Laps=5,Position=1 });
        Eq(10,SeriesScoring.Calculate(series,[ev],"c1",drivers).Count,"Actual racer retained after registration removal");
        ev.DriverAssignments.RemoveAll(a=>a.DriverId=="d2");
        planned.Laps.Add(new LapRecord(1,2,"Racer 2",1,TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(5),new DateTimeOffset(date),"Test",false));
        Eq(10,SeriesScoring.Calculate(series,[ev],"c1",drivers).Count,"Scored lap proves participation without result summary");
        planned.Results.Add(new StandingRow { CarNumber=50,DriverName="Racer 50",Laps=0,Position=50 });
        Eq(10,SeriesScoring.Calculate(series,[ev],"c1",drivers).Count,"Zero-lap placeholder with no signup does not add a row");
        var unused=new RaceDayEventDefinition { EventDate=date,DriverAssignments=[new EventDriverAssignment { ClassId="c1",DriverId="not-selected" }] };
        Eq(10,SeriesScoring.Calculate(series,[ev,unused],"c1",drivers).Count,"Unselected event registration excluded");
        series.EventIds.Add(unused.Id);unused.EventDate=date.AddYears(1);
        Eq(10,SeriesScoring.Calculate(series,[ev,unused],"c1",drivers).Count,"Outside-date registration excluded");
        var before=JsonSerializer.Serialize(ev);
        var report=RaceReports.Series(series,[ev],"c1",drivers);
        Eq(10,report.Sections[0].Rows.Count,"Printed series summary uses same eligibility");
        Eq(false,report.Sections.Any(s=>s.Title.Contains("Unrelated driver")),"Ineligible adjustment details not printed as a racer");
        var signupReport=RaceReports.Series(series,[ev],"c2",drivers,[new ClassDefinition { Id="c2",Name="Stock" }]);
        Eq("Stock",signupReport.Sections[0].Title,"Signup-only printed class label");
        Eq(90,signupReport.Sections[0].Rows.Count,"Signup-only drivers print before heats are generated");
        Eq(before,JsonSerializer.Serialize(ev),"Filtering never modifies registrations or results");
        series.EventIds.Clear();
        Eq(0,SeriesScoring.Calculate(series,[ev],"c1",drivers).Count,"Removed event and leftover adjustments do not keep drivers visible");

        // Round lineups are available before any race or the event is complete.
        var q2=new EventSessionDefinition { Name="Q2",ClassId="c1",ClassName="Breakout",SessionType=SessionType.Qualifying,Round=2,QueueOrder=3,
            Entrants=[new SessionEntrant { CarNumber=1,DriverId="d1",DriverName="Racer 1",TransponderId="3358118",SeedRank=3 }] };
        var main=new EventSessionDefinition { Name="A Main",ClassId="c1",ClassName="Breakout",SessionType=SessionType.Race,QueueOrder=4,Entrants=q2.Entrants };
        var otherClass=new EventSessionDefinition { Name="Stock Q1",ClassId="c2",ClassName="Stock",SessionType=SessionType.Qualifying,Round=1,QueueOrder=2,Entrants=q2.Entrants };
        ev.Sessions.AddRange([q2,main,otherClass]);
        var available=EventReportScope.Available(ev);
        Eq(4,available.Count,"Round picker includes all, Q1, Q2 and mains before completion");
        Eq(4,RaceReports.Lineups(ev).Sections.Count,"Uncompleted event prints all lineups");
        Eq(2,RaceReports.Lineups(ev,scope:new(SessionType.Qualifying,1)).Sections.Count,"Q1 includes both classes");
        Eq(1,RaceReports.Lineups(ev,"c1",new(SessionType.Qualifying,1)).Sections.Count,"Round and class filters combine");
        var q2Report=RaceReports.Lineups(ev,"c1",new(SessionType.Qualifying,2));
        Eq(1,q2Report.Sections.Count,"Q2 only");Eq("3358118",q2Report.Sections[0].Rows[0][2],"Lineup prints transponder");Eq("—",q2Report.Sections[0].Rows[0][3],"No completed prior round prints a dash");
        Eq(true,q2Report.Subtitle.Contains("Qualifying round 2"),"Report labels selected round");
        Eq(1,RaceReports.Lineups(ev,scope:new(SessionType.Race)).Sections.Count,"Mains-only lineup");
        Eq(0,RaceReports.EventStandings(ev,scope:new(SessionType.Qualifying,1)).Sections.Count,"Results exclude uncompleted heats");
        Eq(true,RaceReports.EventStandings(ev).EmptyMessage.Contains("lineups"),"Empty results guide user to placements report");
        planned.ProgressStatus=EventSessionStatus.Completed;
        var completed=RaceReports.EventStandings(ev,"c1",new(SessionType.Qualifying,1));
        Eq(true,completed.Sections.Any(s=>s.Title==planned.Name),"Completed round results print while event still active");
        Eq(0,RaceReports.EventStandings(ev,"c1",new(SessionType.Qualifying,2)).Sections.Count,"Q2 results do not leak Q1 results");
        Eq(1,RaceReports.Lineups(ev,"c1",new(SessionType.Qualifying,1)).Sections.Count,"Completed round lineup remains printable");
        Eq(new EventReportScope(SessionType.Qualifying,1),EventReportScope.InitialSelection(available),"Print dialog defaults to a single round");
        Eq(new EventReportScope(SessionType.Qualifying,2),EventReportScope.InitialSelection(available,new(SessionType.Qualifying,2)),"Chosen round two survives report changes");
        Eq(new EventReportScope(),EventReportScope.InitialSelection(available,new()),"Explicit all-round selection remains available");
        Eq(new EventReportScope(SessionType.Qualifying,1),EventReportScope.InitialSelection(available,new(SessionType.Qualifying,99)),"Missing prior round selects first available round");
        Eq(new EventReportScope(SessionType.Race),EventReportScope.InitialSelection(EventReportScope.Available(new RaceDayEventDefinition { Sessions=[main] })),"Mains-only event defaults to mains");
        foreach (var status in new[] { EventSessionStatus.Planned,EventSessionStatus.Loaded,EventSessionStatus.Completed })
        {
            q2.ProgressStatus=status;
            var clean=RaceReports.Lineups(ev,"c1",new(SessionType.Qualifying,2));
            Eq($"RACE #{q2.RaceNumber} - {q2.ClassName} - HEAT {q2.Heat}",clean.Sections.Single().Title,$"Racer-facing lineup omits {status} status");
        }
        // One print choice carries both the report kind and scope.
        List<PrintReportChoice> Choices(string? classId=null) => PrintReportChoice.Available(ev,classId,false,false);
        bool HasResults(EventReportScope scope,string? classId=null) => Choices(classId).Any(c=>c.ReportType==1 && c.Scope==scope);
        var firstRound=new EventReportScope(SessionType.Qualifying,1);
        q2.ProgressStatus=EventSessionStatus.Planned;
        planned.ProgressStatus=EventSessionStatus.Planned;
        Eq(4,Choices().Count,"Only saved round/mains lineups available before completion");
        Eq(firstRound,Choices()[0].Scope,"Merged print choice defaults to first individual round");
        Eq(2,Choices()[0].ReportType,"Initial merged choice dispatches a lineup");
        Eq(false,HasResults(firstRound),"No unfinished round results offered");
        planned.ProgressStatus=EventSessionStatus.Completed;
        Eq(false,HasResults(firstRound),"Partial all-class round is not yet complete");
        Eq(true,HasResults(firstRound,"c1"),"Completed class results available while other class runs");
        Eq(false,HasResults(firstRound,"c2"),"Other class cannot inherit completed results");
        otherClass.ProgressStatus=EventSessionStatus.Completed;
        otherClass.Results.Add(new StandingRow { CarNumber=1,DriverName="Racer 1",Laps=4,Position=1 });
        Eq(true,HasResults(firstRound),"Completed round adds results while later rounds remain planned");
        Eq(true,Choices().Any(c=>c.Scope==firstRound && c.ReportType==2),"Completion keeps lineup choice available");
        Eq(false,HasResults(new()),"All-round results wait for remaining rounds");
        otherClass.ProgressStatus=EventSessionStatus.Skipped;
        Eq(true,HasResults(firstRound),"Skipped heat does not block completed round");
        planned.ProgressStatus=EventSessionStatus.Skipped;
        Eq(false,HasResults(firstRound),"Entirely skipped round has no completed results choice");
        planned.ProgressStatus=EventSessionStatus.Completed;
        main.ProgressStatus=EventSessionStatus.Completed;
        Eq(false,HasResults(new(SessionType.Race)),"Completed flag with no stored results does not offer empty report");
        main.Results.Add(new StandingRow { CarNumber=1,DriverName="Racer 1",Laps=8,Position=1 });
        Eq(true,HasResults(new(SessionType.Race)),"Completed mains are offered");
        q2.ProgressStatus=EventSessionStatus.Skipped;
        Eq(true,HasResults(new()),"All-round results offered when all remaining races completed");
        Eq(0,Choices("missing-class").Count,"Unavailable class does not offer blank event reports");
        Eq(0,PrintReportChoice.Available(null,null,false,false).Count,"No data means no report choices");
        var auxiliary=PrintReportChoice.Available(null,null,true,true);
        Eq(true,auxiliary.Select(c=>c.ReportType).SequenceEqual(new[]{0,3}),"Practice and series remain accessible in merged selector");
        Eq(1,RaceReports.Lineups(ev,"c1",Choices("c1").First(c=>c.Scope==firstRound && c.ReportType==2).Scope).Sections.Count,"Merged lineup choice dispatches selected round");
        Eq(true,RaceReports.EventStandings(ev,"c1",Choices("c1").First(c=>c.Scope==firstRound && c.ReportType==1).Scope).Sections.Any(s=>s.Title==planned.Name),"Merged results choice dispatches selected round");
        var prior = new EventSessionDefinition { ClassId="print",Round=1,ProgressStatus=EventSessionStatus.Completed,
            Entrants=[new() { DriverId="stable",CarNumber=7 }], Results=[new() { CarNumber=7,Laps=42,TotalTime="4:02.345" }] };
        var next = new EventSessionDefinition { ClassId="print",Round=2,Entrants=[new() { DriverId="stable",CarNumber=1,DriverName="Renamed driver",TransponderId="123" }] };
        var future = new EventSessionDefinition { ClassId="print",Round=3,ProgressStatus=EventSessionStatus.Completed,
            Entrants=[new() { DriverId="stable",CarNumber=7 }], Results=[new() { CarNumber=7,Laps=99,TotalTime="9:00.000" }] };
        var printEvent = new RaceDayEventDefinition { Sessions=[prior,next,future] };
        Eq("Q1 · 42 / 4:02.345",RaceReports.PreviousRoundResult(printEvent,next,"stable"),"Prior round survives car/name changes and ignores future rounds");
        Eq("—",RaceReports.PreviousRoundResult(printEvent,prior,"stable"),"First round has no invented prior result");
        Eq("—",RaceReports.PreviousRoundResult(printEvent,next,"new"),"New driver has no invented result");
        var rated = RaceReports.Lineups(printEvent,scope:new(SessionType.Qualifying,2),ratings:[new() { ClassId="other",DriverId="stable",Rating=999,Safety=1 },new() { ClassId="print",DriverId="stable",Rating=1674,Safety=4.21 }]);
        Eq("1674 / 4.21",rated.Sections[0].Rows[0][4],"SOF/SR uses individual class rating and safety");
        Eq(false,rated.Sections[0].Columns.Contains("Seed"),"Seed replaced by ratings");
        Eq("1500 / 3.00*",RaceReports.Lineups(printEvent,scope:new(SessionType.Qualifying,2)).Sections[0].Rows[0][4],"Unrated driver starting values marked provisional");
        var m1 = new EventSessionDefinition { ClassId="print",SessionType=SessionType.Race,MainLeg=1,Entrants=[new() { DriverId="stable",CarNumber=2 }] };
        printEvent.Sessions.Add(m1);
        Eq("Q3 · 99 / 9:00.000",RaceReports.PreviousRoundResult(printEvent,m1,"stable"),"First main uses latest completed qualifying round");
        m1.ProgressStatus=EventSessionStatus.Completed;m1.Results=[new() { CarNumber=2,Laps=48,TotalTime="5:01.010" }];
        var m2 = new EventSessionDefinition { ClassId="print",SessionType=SessionType.Race,MainLeg=2 };
        Eq("M1 · 48 / 5:01.010",RaceReports.PreviousRoundResult(printEvent,m2,"stable"),"Repeated main uses prior completed main");
        Eq(ClassPrintColors.Choices[1].Hex,ClassPrintColors.Safe("#BDF1BB"),"Existing pastel choice migrates to same vivid color");
        foreach (var color in ClassPrintColors.Choices)
        {
            double Linear(int n) { var v=n/255d;return v<=.04045 ? v/12.92 : Math.Pow((v+.055)/1.055,2.4); }
            var red=Convert.ToInt32(color.Hex.Substring(1,2),16);var green=Convert.ToInt32(color.Hex.Substring(3,2),16);var blue=Convert.ToInt32(color.Hex.Substring(5,2),16);
            var luminance=.2126*Linear(red)+.7152*Linear(green)+.0722*Linear(blue);
            Eq(true,(luminance+.05)/.05>=4.5,"Black text readable on "+color.Name);
        }
        return checks;
    }
}
