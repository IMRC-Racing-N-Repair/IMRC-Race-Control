using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

internal static class RacePBDisplayChecks
{
    public static int Run()
    {
        int checks=0;
        void Check(bool condition,string message) { if(!condition) throw new Exception("Race PB display: "+message); checks++; }
        var oldLapped=new DriverPersonalBest("d","c",RaceFormat.Laps,50,50,260,
            DateTimeOffset.UtcNow,"track","e","s","fixture");
        var paceAhead=RacePersonalBestDisplay.Evaluate(oldLapped,RaceFormat.Laps,50,10,50,false);
        Check(paceAhead.Text=="PB: 50/4:20.000" && !paceAhead.IsNewRecord,
            "shows previous 50 lap PB only when projected pace is better");
        Check(RacePersonalBestDisplay.Evaluate(oldLapped,RaceFormat.Laps,50,10,60,false).Text=="",
            "does not show PB when projected pace is slower");
        Check(RacePersonalBestDisplay.Evaluate(oldLapped,RaceFormat.Laps,50,50,259.4,true)
            is {Text:"PB: 50/4:19.400",IsNewRecord:true},"finished new PB is confirmed and highlighted");
        Check(RacePersonalBestDisplay.Evaluate(oldLapped,RaceFormat.Laps,50,50,260,true).Text=="",
            "matching PB is not new");
        Check(RacePersonalBestDisplay.Evaluate(oldLapped,RaceFormat.Laps,50,49,230,true).Text=="",
            "incomplete fixed-lap race does not claim PB");
        Check(RacePersonalBestDisplay.Evaluate(null,RaceFormat.Laps,50,10,50,false).Text=="",
            "no prior record is not called ahead of PB pace");
        Check(RacePersonalBestDisplay.Evaluate(null,RaceFormat.Laps,50,50,250,true).IsNewRecord,
            "first completed result becomes initial PB");
        var oldTimed=new DriverPersonalBest("d","c",RaceFormat.Timed,300,40,302,
            DateTimeOffset.UtcNow,"track","e","s","fixture");
        Check(RacePersonalBestDisplay.Evaluate(oldTimed,RaceFormat.Timed,300,20,140,false).Text=="PB: 40/5:02.000",
            "timed faster lap pace projects more laps than the old result");
        Check(RacePersonalBestDisplay.Evaluate(oldTimed,RaceFormat.Timed,300,20,155,false).Text=="",
            "timed pace at fewer laps does not claim a PB");
        Check(RacePersonalBestDisplay.Evaluate(oldTimed,RaceFormat.Timed,300,41,310,true).IsNewRecord,
            "timed races compare laps before elapsed completion time");
        Check(!RacePersonalBestDisplay.Evaluate(oldTimed,RaceFormat.Timed,480,20,140,true).IsNewRecord,
            "different race duration is not comparable");
        return checks;
    }
}
