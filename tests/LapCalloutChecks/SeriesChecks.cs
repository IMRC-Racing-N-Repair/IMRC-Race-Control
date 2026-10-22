using System.Text.Json;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;

public static class SeriesChecks
{
    public static async Task<int> Run(bool isolated)
    {
        int checks=0;
        void Eq<T>(T expected,T actual,string label) { if (!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"{label}: expected {expected}, got {actual}"); checks++; }
        var date=new DateTime(2026,1,3);
        EventSessionDefinition Session(SessionType type,int round,int heat,int leg,params (string Id,int Position,int Laps,double Fast)[] results)
        {
            var session=new EventSessionDefinition { ClassId="c1",ClassName="Breakout",Name=$"{type} {round}/{heat}/{leg}",SessionType=type,Round=round,Heat=heat,MainLeg=leg,ProgressStatus=EventSessionStatus.Completed,MainCountBest=1 };
            int car=0;
            foreach (var r in results)
            {
                car++;
                session.Entrants.Add(new SessionEntrant { DriverId=r.Id,DriverName=r.Id,CarNumber=car,SeedRank=car,TransponderId=$"TX{car}" });
                session.Results.Add(new StandingRow { CarNumber=car,Position=r.Position,DriverName=r.Id,Laps=r.Laps,BestLap=r.Fast.ToString("0.000"),TotalTime=(r.Laps*r.Fast).ToString("0.000") });
                for (var n=1;n<=r.Laps;n++) session.Laps.Add(new LapRecord(session.Laps.Count+1,car,r.Id,n,TimeSpan.FromSeconds(r.Fast),TimeSpan.FromSeconds(n*r.Fast),new DateTimeOffset(date).AddSeconds(n*r.Fast),"Test",n==r.Laps));
            }
            return session;
        }
        var ev=new RaceDayEventDefinition { Name="Week 1",EventDate=date,Status=EventStatus.Completed,ClassIds=["c1"],Sessions=[
            Session(SessionType.Qualifying,1,1,1,("A",1,10,4),("B",2,9,5)),
            Session(SessionType.Qualifying,2,1,1,("A",2,9,5),("B",1,10,4)),
            Session(SessionType.Race,1,1,1,("A",1,20,4),("B",2,19,5)) ],
            Rankings=[new EventRankingEntry { ClassId="c1",DriverId="A",DriverName="A",Rank=1 },new EventRankingEntry { ClassId="c1",DriverId="B",DriverName="B",Rank=2 }] };
        var series=new RaceSeriesDefinition { StartDate=date,EndDate=date.AddYears(1),EventIds=[ev.Id],PointsSettings=new() {
            Qualifying=new StagePointsSettings { Enabled=true,PositionPoints="10,8",PointsPerLap=0.5m,ParticipationPoints=2,WinBonus=3,FastLapBonus=4 },
            Mains=new StagePointsSettings { Enabled=true,PositionPoints="100,90",PointsPerLap=1,ParticipationPoints=5,WinBonus=10,FastLapBonus=2 },TqBonus=7,EventAttendancePoints=1 } };
        var scored=SeriesScoring.Calculate(series,[ev],"c1");
        var a=scored.Single(r=>r.DriverId=="A");
        Eq(28.5m,a.Weeks[0].Qualifying,"Qual position, lap, participation, win, tied fast lap");
        Eq(137m,a.Weeks[0].Mains,"Independent main awards");
        Eq(8m,a.Weeks[0].Bonuses,"TQ and once-per-event attendance");
        Eq(173.5m,a.Points,"Combined weekly score");
        Eq(23.5m,scored.Single(r=>r.DriverId=="B").Weeks[0].Qualifying,"Fastest qualifying lap tie awards both drivers");
        // A combined/cached ranking must never override the best single qualifying run for TQ.
        var tqEvent = new RaceDayEventDefinition { Name="TQ regression", EventDate=date, Status=EventStatus.Completed,
            ClassIds=["c1"], Sessions=[
                Session(SessionType.Qualifying,1,1,1,("A",1,12,4)),
                Session(SessionType.Qualifying,1,2,1,("B",1,11,3)),
                Session(SessionType.Qualifying,2,1,1,("A",1,5,4)),
                Session(SessionType.Qualifying,2,2,1,("B",1,11,3)) ],
            Rankings=[new EventRankingEntry { ClassId="c1", DriverId="B", Rank=1 },new EventRankingEntry { ClassId="c1", DriverId="A", Rank=2 }] };
        var tqSeries = new RaceSeriesDefinition { StartDate=date, EndDate=date.AddDays(1), EventIds=[tqEvent.Id],
            PointsSettings=new() { TqBonus=9, Qualifying=new() { MinimumLaps=1 }, Mains=new() { Enabled=false } } };
        foreach (var mode in Enum.GetValues<QualifyingPointsMode>())
        {
            tqSeries.PointsSettings.QualifyingMode=mode;
            var tqScores=SeriesScoring.Calculate(tqSeries,[tqEvent],"c1");
            Eq(9m,tqScores.Single(r=>r.DriverId=="A").Weeks[0].Bonuses,$"Best single run earns TQ in {mode}");
            Eq(0m,tqScores.Single(r=>r.DriverId=="B").Weeks[0].Bonuses,$"Combined laps cannot earn TQ in {mode}");
        }
        tqEvent.Sessions.Add(Session(SessionType.Qualifying,3,2,1,("B",1,12,3.9)));
        Eq(9m,SeriesScoring.Calculate(tqSeries,[tqEvent],"c1").Single(r=>r.DriverId=="B").Weeks[0].Bonuses,"Equal laps: lower elapsed time across all heats wins TQ");
        tqEvent.Sessions.Last().ProgressStatus=EventSessionStatus.Planned;
        Eq(9m,SeriesScoring.Calculate(tqSeries,[tqEvent],"c1").Single(r=>r.DriverId=="A").Weeks[0].Bonuses,"Uncompleted qualifier excluded from TQ");

        series.PointsSettings.QualifyingMode=QualifyingPointsMode.EveryRound;
        Eq(36.5m,SeriesScoring.Calculate(series,[ev],"c1").Single(r=>r.DriverId=="A").Weeks[0].Qualifying,"Every-round positions, laps counted once");
        series.PointsSettings.QualifyingMode=QualifyingPointsMode.BestRound;
        Eq(28.5m,SeriesScoring.Calculate(series,[ev],"c1").Single(r=>r.DriverId=="A").Weeks[0].Qualifying,"Best qualifying round");
        series.PointsSettings.Qualifying.MinimumLaps=20;
        var min=SeriesScoring.Calculate(series,[ev],"c1").Single(r=>r.DriverId=="A");
        Eq(0m,min.Weeks[0].Qualifying,"Minimum qualifying laps enforced"); Eq(1m,min.Weeks[0].Bonuses,"TQ minimum laps enforced");
        series.PointsSettings.Qualifying.Enabled=false; series.PointsSettings.Mains.Enabled=false; series.PointsSettings.TqBonus=0; series.PointsSettings.EventAttendancePoints=0;
        Eq(0m,SeriesScoring.Calculate(series,[ev],"c1")[0].Points,"Disabled stages and bonuses award no points");
        Eq(0,SeriesScoring.Calculate(series,[ev],"other").Count,"Classes do not mix");
        Eq(false,SeriesScoring.TrySchedule("10,,5",out _),"Reject empty position slot");
        Eq(false,SeriesScoring.TrySchedule("10,-5",out _),"Reject negative position points");
        Eq(true,SeriesScoring.TrySchedule("10,8.5,7",out var decimalSchedule),"Decimal schedule accepted"); Eq(8.5m,decimalSchedule[1],"Decimal exactness");

        series.PointsSettings.Mains = new StagePointsSettings { Enabled=true, PositionPoints="100", UnlistedPositionPoints=7 };
        Eq(7m,SeriesScoring.Calculate(series,[ev],"c1").Single(r=>r.DriverId=="B").Points,"Unlisted main position award");
        series.PointsSettings.Mains.MinimumLaps=20;
        Eq(0m,SeriesScoring.Calculate(series,[ev],"c1").Single(r=>r.DriverId=="B").Points,"Minimum main laps eligibility");
        var originalResults=JsonSerializer.Serialize(ev);
        SeriesScoring.Calculate(series,[ev],"c1");
        Eq(originalResults,JsonSerializer.Serialize(ev),"Scoring never changes event results");

        // Best eight out of ten, adjustments before drops and after drops.
        var weeks=Enumerable.Range(1,10).Select(i=>new RaceDayEventDefinition { Name=$"Week {i}",EventDate=date.AddDays(i*7),Status=EventStatus.Completed,ClassIds=["c1"],Sessions=[Session(SessionType.Race,1,1,1,("A",1,i,4),("B",2,1,5))] }).ToList();
        var season=new RaceSeriesDefinition { StartDate=date,EndDate=date.AddYears(1),MaximumEvents=10,BestEventCount=8,EventIds=weeks.Select(w=>w.Id).ToList(),PointsSettings=new() { Mains=new StagePointsSettings { Enabled=true,PositionPoints="0,0",PointsPerLap=1 } } };
        var best=SeriesScoring.Calculate(season,weeks,"c1").Single(r=>r.DriverId=="A");
        Eq(52m,best.Points,"Best eight of ten total"); Eq(8,best.CountedEvents,"Eight counted events"); Eq(10,best.Events,"Attendance includes drops"); Eq(3m,best.DroppedPoints,"Two dropped weeks points"); Eq(2,best.Weeks.Count(w=>w.Status=="DROPPED"),"Dropped weeks remain visible");
        Eq("DROPPED",best.Weeks[0].Status,"Lowest first week dropped");
        season.Adjustments.Add(new SeriesPointsAdjustment { DriverId="A",DriverName="A",ClassId="c1",EventId=weeks[0].Id,EventName="Week 1",Amount=100,Note="Scoring correction" });
        season.Adjustments.Add(new SeriesPointsAdjustment { DriverId="A",DriverName="A",ClassId="c1",Amount=-5,Note="Penalty" });
        var adjusted=SeriesScoring.Calculate(season,weeks,"c1").Single(r=>r.DriverId=="A");
        Eq(145m,adjusted.Points,"Week correction before best eight and series penalty after"); Eq("COUNTED",adjusted.Weeks[0].Status,"Corrected week can become counted"); Eq(5m,adjusted.DroppedPoints,"New lowest weeks dropped"); Eq(true,adjusted.Weeks[0].Notes.Contains("Scoring correction"),"Adjustment note exposed");
        var otherClassAdjustment=new SeriesPointsAdjustment { DriverId="A",DriverName="A",ClassId="c2",Amount=999,Note="Other class" };season.Adjustments.Add(otherClassAdjustment);
        Eq(145m,SeriesScoring.Calculate(season,weeks,"c1").Single(r=>r.DriverId=="A").Points,"Adjustment is class-specific");
        weeks[9].Status=EventStatus.Active;
        var pending=SeriesScoring.Calculate(season,weeks,"c1").Single(r=>r.DriverId=="A");
        Eq("PENDING",pending.Weeks[9].Status,"Active event waits for completion"); Eq(138m,pending.Points,"Pending event excluded from points and drops");
        weeks[9].Status=EventStatus.Completed;
        season.EventIds.Remove(weeks[9].Id);
        Eq(9,SeriesScoring.Calculate(season,weeks,"c1")[0].Weeks.Count,"Removed week disappears");
        season.EventIds.Add(weeks[9].Id); season.EndDate=weeks[8].EventDate;
        Eq(9,SeriesScoring.SelectedEvents(season,weeks).Count,"Date range respected");season.EndDate=date.AddYears(1);
        season.BestEventCount=0;
        Eq(150m,SeriesScoring.Calculate(season,weeks,"c1").Single(r=>r.DriverId=="A").Points,"Count all with adjustments");
        season.BestEventCount=8;
        var ties=new RaceSeriesDefinition { StartDate=date,EndDate=date.AddYears(1),EventIds=[ev.Id],PointsSettings=new() { Mains=new StagePointsSettings { Enabled=false },TieBreak=SeriesTieBreak.BestWeek } };
        Eq(1,SeriesScoring.Calculate(ties,[ev],"c1").Last().Position,"Unresolved ties share place");
        ties.PointsSettings.TieBreak=SeriesTieBreak.WinsThenBestFinish;
        Eq("A",SeriesScoring.Calculate(ties,[ev],"c1").First().DriverId,"Win tie-break");

        // Main series combines legs and keeps the existing race-day algorithm.
        var multi=new RaceDayEventDefinition { Name="Triple mains",EventDate=date,Status=EventStatus.Completed,ClassIds=["c1"],Sessions=[
            Session(SessionType.Race,1,1,1,("A",1,20,4),("B",2,19,5)),
            Session(SessionType.Race,1,1,2,("A",2,19,5),("B",1,21,4)),
            Session(SessionType.Race,1,1,3,("A",2,19,5),("B",1,21,4)),
            Session(SessionType.Race,1,2,1,("C",1,25,4),("A",2,24,5)) ] };
        foreach(var s in multi.Sessions.Where(s=>s.Heat==1)) { s.MainCountBest=2;s.MainRunCount=3; }
        var order=SeriesScoring.MainOrder(multi,"c1");
        Eq("B",order[0].DriverId,"Best two of three mains determines winner"); Eq("A",order[1].DriverId,"Main runner-up"); Eq("C",order[2].DriverId,"B main follows A main"); Eq(3,order.Count,"Bumped driver not double-counted");
        var old=JsonSerializer.Deserialize<RaceSeriesDefinition>("{\"PointsCsv\":\"50,40,30\"}")!;
        Eq("50,40,30",old.EffectivePoints.Mains.PositionPoints,"Legacy schedule migration"); Eq(true,old.EffectivePoints.LegacyQualifyingFallback,"Legacy fallback retained");
        old.StartDate=date;old.EndDate=date.AddYears(1);old.EventIds=[multi.Id];
        Eq(50m,SeriesScoring.Calculate(old,[multi],"c1").First().Points,"Legacy main points retained");

        var lineup=RaceReports.Lineups(multi);
        Eq(4,lineup.Sections.Count,"Print all main lineups");Eq("TX1",lineup.Sections[0].Rows[0][2],"Printed transponder");Eq(0,RaceReports.Lineups(multi,"none").Sections.Count,"Print class filter");
        var eventReport=RaceReports.EventStandings(multi);Eq("B",eventReport.Sections[0].Rows[0][1],"Print overall repeated-main winner");
        var seriesReport=RaceReports.Series(season,weeks,"c1");
        Eq(true,seriesReport.Sections.SelectMany(s=>s.Rows).Any(r=>r.Contains("DROPPED")),"Printed dropped weeks");
        Eq(true,seriesReport.Sections.SelectMany(s=>s.Rows).Any(r=>r.Contains("Scoring correction")),"Printed adjustment reason");
        Eq(false,seriesReport.Sections.SelectMany(s=>s.Rows).Any(r=>r.Contains("Other class")),"Printed class isolation");
        var practice=RaceReports.Practice("A",date,date,[(new PracticeHistoryRow { DriverId="A",DriverName="A",StartedAt=new DateTimeOffset(date),EndedAt=new DateTimeOffset(date).AddSeconds(5),LapCount=1 }, new[] { new PracticeHistoryLapRow { LapNumber=1,LapTimeSeconds=4.339,Timestamp=new DateTimeOffset(date).AddSeconds(5) } })]);
        Eq("4.339",practice.Sections[0].Rows[0][1],"Print full practice precision");

        if (isolated)
        {
            var db=new RaceControlDatabaseService();
            if (!db.DatabasePath.StartsWith("/tmp/imrc-callout-test-data/")) throw new Exception("Non-isolated database");
            await db.SaveApplicationStateAsync(new AppSettings { Series=[season],Events=weeks });
            var loaded=(await new RaceControlDatabaseService().LoadApplicationStateAsync())!;
            var restored=loaded.Series.Single();
            Eq(8,restored.BestEventCount,"Best count persists");Eq(10,restored.MaximumEvents,"Event limit persists");Eq(10,restored.EventIds.Count,"Manual weekly choices persist");Eq(3,restored.Adjustments.Count,"Adjustment history persists");Eq("Scoring correction",restored.Adjustments[0].Note,"Reason persists");
            Eq(145m,SeriesScoring.Calculate(restored,loaded.Events,"c1").Single(r=>r.DriverId=="A").Points,"SQLite restored standings match");
            restored.EventIds.RemoveAt(9);restored.PointsSettings!.Mains.PointsPerLap=0.5m;
            await db.SaveApplicationStateAsync(loaded);
            var reopened=(await new RaceControlDatabaseService().LoadApplicationStateAsync())!;
            Eq(9,reopened.Series[0].EventIds.Count,"Removed week persists after second restart");Eq(0.5m,reopened.Series[0].PointsSettings!.Mains.PointsPerLap,"Fractional points setting persists");
            var history=new PracticeHistoryService();
            if (!history.DatabasePath.StartsWith("/tmp/imrc-callout-test-data/")) throw new Exception("Non-isolated practice database");
            var reportDriver=Guid.NewGuid().ToString("N");
            var savedStintIds=new List<string>();
            foreach (var id in new[] { reportDriver,reportDriver,"other-driver" })
            {
                var card=new PracticeCardViewModel { DriverId=id,DriverName=id,StartedAt=new DateTimeOffset(date),LastCrossingAt=new DateTimeOffset(date).AddSeconds(5) };
                card.AddLap(new PracticeLapEntry { LapNumber=1,LapTime=TimeSpan.FromSeconds(4.339),Timestamp=card.LastCrossingAt,Source="Test" });
                await history.SaveStintAsync(card,card.LastCrossingAt);
                if (id==reportDriver) savedStintIds.Add(card.StintId);
            }
            var driverStints=await history.LoadRecentStintsAsync(limit:1,driverId:reportDriver);
            Eq(2,driverStints.Count,"Practice report query retrieves all stints for the driver");
            Eq(true,driverStints.All(s=>s.DriverId==reportDriver),"Practice query excludes other drivers");
            Eq(4.339,(await history.LoadLapsAsync(savedStintIds[0])).Single().LapTimeSeconds,"Practice report retrieves exact saved lap time");
        }
        return checks;
    }
}
