using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
internal static class NextRaceChecks
{
    public static int Run()
    {
        int count=0;
        void Check(bool v,string label){if(!v)throw new Exception("Next race: "+label);count++;}
        var current=new EventSessionDefinition{Id="a",QueueOrder=1,ClassId="stock",RaceNumber=1,Heat=1};
        var skipped=new EventSessionDefinition{Id="b",QueueOrder=2,ProgressStatus=EventSessionStatus.Skipped};
        var next=new EventSessionDefinition{Id="c",QueueOrder=3,ClassId="stock",ClassName="Stock",RaceNumber=2,Heat=2,Entrants=[new(){CarNumber=2,DriverName="Mike"},new(){CarNumber=1,DriverName="Josh",Nickname="Yoshi"}]};
        var races=new[]{next,skipped,current};
        var preview=NextRacePreview.Build(races,"a");
        Check(preview.Heading.Contains("Race 2 · Stock · Q1, Heat 2/2"),"queue order and skipped races");
        Check(preview.Names.StartsWith("1  Yoshi")&&preview.Names.EndsWith("2  Mike"),"car order and display names");
        current.ProgressStatus=EventSessionStatus.Completed;
        Check(NextRacePreview.Build(races,"a")==preview,"finished screen retains upcoming lineup");
        next.Entrants[0].Nickname="Speedy";
        Check(NextRacePreview.Build(races,"a").Names.Contains("Speedy"),"lineup edits refreshed");
        Check(NextRacePreview.Build(races,"c").Names.Contains("Final race"),"last race message");
        Check(NextRacePreview.Build([],null).Names=="No races scheduled","empty schedule");
        Check(NextRacePreview.Build(races,null).Heading.Contains("Race 2"),"no selected race uses first pending");
        next.SessionType=SessionType.Race;next.MainLeg=2;next.MainRunCount=3;next.Heat=1;next.Entrants.Clear();
        var main=NextRacePreview.Build(races,"a");
        Check(main.Heading.Contains("M2, A-Main 2/3"),"main labels and round transition");
        Check(main.Names=="Lineup pending","unfilled main");
        return count;
    }
}
