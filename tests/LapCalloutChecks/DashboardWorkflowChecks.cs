using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

public static class DashboardWorkflowChecks
{
    public static int Run()
    {
        int checks=0;
        void Eq<T>(T expected,T actual,string name)
        {
            if (!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"{name}: expected {expected}, got {actual}");
            checks++;
        }
        var state=new RaceCheckInState();
        var racers=new List<RacerEntry> {
            new() { CarNumber=1, DriverName="Josh Ingersoll",Nickname="Yoshi" },
            new() { CarNumber=2, DriverName="Clint" },
            new() { CarNumber=3, DriverName="Disabled racer",Enabled=false } };
        state.SelectSession("Q1-race1");
        Eq("Yoshi,Clint",string.Join(',',state.MissingNames(racers)),"Start warning names only enabled racers");
        Eq("Yoshi is not checked in.\nClint is not checked in.",RaceCheckInState.WarningMessage(state.MissingNames(racers)),"Warning lists each missing racer by display name");
        state.CheckedCars.Add(1);
        Eq("Clint",state.MissingNames(racers).Single(),"Only unchecked racer is warned");
        state.SelectSession("Q1-race1");
        Eq(true,state.CheckedCars.Contains(1),"Refreshing same loaded race retains check-in");
        state.SelectSession("Q1-race2");
        Eq(0,state.CheckedCars.Count,"Switching race clears check-ins");
        state.CheckedCars.Add(2);
        state.SelectSession("Q1-race1");
        Eq(0,state.CheckedCars.Count,"Returning to unfinished race requires fresh check-in");
        state.CheckedCars.Add(1);state.CheckedCars.Add(2);
        Eq(0,state.MissingNames(racers).Count,"All enabled racers checked in means no warning");
        state.SelectSession("Q1-race1",restart:true);
        Eq(0,state.CheckedCars.Count,"Restart of same race clears check-ins");
        state.CheckedCars.Add(1);
        var engine=new RaceEngine();
        engine.Start(racers,TimeSpan.FromMinutes(5),50,RaceFormat.Timed,TimeSpan.FromSeconds(1),SessionType.Qualifying,StartStyle.IndividualCrossing,DateTimeOffset.Now);
        engine.FalseStart();
        Eq(RaceState.FalseStart,engine.State,"False start resets timing state");
        Eq(true,state.CheckedCars.Contains(1),"False start keeps checked-in racer");
        Eq(false,state.CheckedCars.Contains(2),"False start never checks in missing racer");
        Eq("Clint",state.MissingNames(racers).Single(),"Retry still warns for missing racer");
        state.Clear();
        Eq(0,state.CheckedCars.Count,"Roster invalidation clears check-ins");
        Eq("",state.SessionId,"Roster invalidation clears session identity");

        EventSessionDefinition Session(int round,int number,int queue,SessionType type=SessionType.Qualifying,int leg=1) => new() {
            ClassId="c"+number,ClassName="25.5 FTT",Round=round,RaceNumber=number,QueueOrder=queue,Heat=number,MainLeg=leg,SessionType=type,
            Name="Legacy long class Q label",Entrants=[new() {CarNumber=1,DriverName="Yoshi"}] };
        var sessions=new[] { Session(2,1,1),Session(1,2,4),Session(1,1,3),Session(2,2,2),Session(1,1,7,SessionType.Race,2),Session(1,2,5,SessionType.Race,1),Session(1,1,6,SessionType.Race,1) };
        var queue=string.Join(',',sessions.Select(s=>s.QueueOrder));
        var ordered=RaceSchedule.DisplayOrder(sessions).ToList();
        Eq("Q1:1,Q1:2,Q2:1,Q2:2,M1:1,M1:2,M2:1",string.Join(',',ordered.Select(s=>$"{s.RoundLabel}:{s.RaceNumber}")),"Event display sorts round first then numeric race number");
        Eq(queue,string.Join(',',sessions.Select(s=>s.QueueOrder)),"Display sorting leaves execution queue intact");
        var ev=new RaceDayEventDefinition {Name="Race Day",Sessions=sessions.ToList()};
        var print=RaceReports.Lineups(ev);
        Eq(true,print.IsLineup,"Lineups use dedicated paginated layout");
        Eq("Qualifying Round 1",print.Sections[0].RoundHeading,"Full qualifying round heading for page top");
        Eq("RACE #1 - 25.5 FTT - HEAT 1",print.Sections[0].Title,"Race title contains number class and heat without redundant Q label");
        Eq("Qualifying Round 2",print.Sections[2].RoundHeading,"Next round gets distinct page heading");
        Eq("Main Round 1",print.Sections[4].RoundHeading,"Main run uses full round heading");
        Eq("RACE #1 - 25.5 FTT - A-MAIN",print.Sections[4].Title,"Main letter retained in race heading");
        Eq("Main Round 2",print.Sections[6].RoundHeading,"Second main run is separate from first");
        Eq("Yoshi",print.Sections[0].Rows.Single()[1],"Driver lineup preserved in new report");
        Eq(false,RaceReports.EventStandings(ev).IsLineup,"Results retain their own layout");
        return checks;
    }
}
