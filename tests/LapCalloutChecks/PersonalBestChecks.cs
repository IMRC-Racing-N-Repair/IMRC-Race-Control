using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using System.Text.Json;

public static class PersonalBestChecks
{
    public static int Run()
    {
        static EventSessionDefinition Session(string id, string cls, RaceFormat format, int distance,
            int laps, string elapsed, string? status = "FINISHED")
        {
            return new EventSessionDefinition
            {
                Id=id, ClassId=cls, ClassName=cls, RaceFormat=format,
                RaceMinutesOverride=format==RaceFormat.Timed?distance/60.0:null,
                TargetLaps=format==RaceFormat.Laps?distance:50,
                ProgressStatus=EventSessionStatus.Completed,
                Entrants=[new SessionEntrant { DriverId="driver1", CarNumber=5 }],
                Results=[new StandingRow { CarNumber=5, Laps=laps, TotalTime=elapsed, Status=status??"" }]
            };
        }
        var a = new RaceDayEventDefinition
        {
            Id="e1", Status=EventStatus.Completed,
            Sessions=[
                Session("timed-old","classA",RaceFormat.Timed,300,40,"5:02.100"),
                Session("timed-more","classA",RaceFormat.Timed,300,41,"5:03.000"),
                Session("timed-less","classA",RaceFormat.Timed,300,39,"4:58.000"),
                Session("timed-other-length","classA",RaceFormat.Timed,480,60,"8:04.000"),
                Session("timed-other-class","classB",RaceFormat.Timed,300,44,"5:04.000"),
                Session("laps-old","classA",RaceFormat.Laps,50,50,"4:21.200"),
                Session("laps-faster","classA",RaceFormat.Laps,50,50,"4:19.500"),
                Session("laps-incomplete","classA",RaceFormat.Laps,50,49,"4:00.000"),
                Session("laps-other-target","classA",RaceFormat.Laps,75,75,"6:25.000"),
                Session("laps-dnf","classA",RaceFormat.Laps,50,50,"3:50.000","DNF"),
                Session("timed-without-time","classC",RaceFormat.Timed,300,40,"—"),
                Session("timed-without-distance","classD",RaceFormat.Timed,0,40,"5:00.000")
            ]
        };
        var list=PersonalBests.FromEvents([a], [new ClassDefinition { Id="classD", RaceMinutes=5 }]);
        if(list.Count!=6) throw new Exception("PB group count or invalid-result rejection incorrect");
        var timed=PersonalBests.Lookup(list,"driver1","classA",RaceFormat.Timed,300);
        var lapped=PersonalBests.Lookup(list,"driver1","classA",RaceFormat.Laps,50);
        if(timed?.Laps!=41 || Math.Abs(timed.TotalSeconds-303)>0.0001 ||
           lapped?.Laps!=50 || Math.Abs(lapped.TotalSeconds-259.5)>0.0001)
            throw new Exception("Timed laps-first or fixed-laps fastest-time comparison failed");
        if(!PersonalBests.Beats(timed,42,310,RaceFormat.Timed,300) ||
            PersonalBests.Beats(timed,40,280,RaceFormat.Timed,300) ||
            PersonalBests.Beats(timed,41,303,RaceFormat.Timed,300) ||
            !PersonalBests.Beats(timed,41,302.5,RaceFormat.Timed,300) ||
            PersonalBests.Beats(lapped,49,200,RaceFormat.Laps,50) ||
            PersonalBests.Beats(lapped,50,259.5,RaceFormat.Laps,50) ||
            !PersonalBests.Beats(lapped,50,259.4,RaceFormat.Laps,50) ||
            PersonalBests.Beats(timed,60,450,RaceFormat.Timed,480))
            throw new Exception("Race format and distance PB threshold comparison failed");
        var saved=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(
            new AppSettings { DriverPersonalBests=list }))!;
        if(saved.DriverPersonalBests.Count!=6 ||
            PersonalBests.Lookup(saved.DriverPersonalBests,"driver1","classA",RaceFormat.Laps,50)?.TotalSeconds!=259.5)
            throw new Exception("PB persistence round-trip failed");
        return 5;
    }
}
