using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
namespace IMRCRaceControl;
public partial class MainWindow
{
    private bool ValidateCombinedLineup(Dialogs.RoundMainBuilderDialog dialog)
    {
        if(_activeEvent is null)return false;
        var replacements=dialog.OrderOnlySave ? new List<EventSessionDefinition>() : dialog.BuiltSessions.ToList();
        var scoped=_activeEvent.Sessions.Where(s=>!dialog.OrderOnlySave && dialog.ReplaceClassIds.Contains(s.ClassId) && s.SessionType==dialog.BuiltSessionType && (s.SessionType==SessionType.Race || s.Round==dialog.BuiltRound)).Select(s=>s.Id).ToHashSet();
        var future=_activeEvent.Sessions.Where(s=>!scoped.Contains(s.Id)).Concat(replacements).Select(CombinedRaces.Clone).ToList();
        foreach(var session in future)if(dialog.PendingCombinedRaces.TryGetValue(session.Id,out var id))session.CombinedRaceId=id;
        try
        {
            foreach(var group in future.Where(s=>s.CombinedRaceId.Length>0).GroupBy(s=>s.CombinedRaceId))
                if(group.Count()>1 && group.All(RaceLineupOrder.Movable))CombinedRaces.Validate(group.ToList(),Classes.ToList());
            return true;
        }
        catch(InvalidOperationException ex) {System.Windows.MessageBox.Show(this,ex.Message,"Combined race cannot be saved");return false;}
    }
    private void ApplyCombinedLineup(Dialogs.RoundMainBuilderDialog dialog)
    {
        if(_activeEvent is null)return;
        foreach(var s in _activeEvent.Sessions)
            if(dialog.PendingCombinedRaces.TryGetValue(s.Id,out var id))
            {s.CombinedRaceId=id;s.CombinedRaceOrder=s.QueueOrder;}
        foreach(var group in _activeEvent.Sessions.Where(s=>s.CombinedRaceId.Length>0).GroupBy(s=>s.CombinedRaceId))
        {
            if(group.Count()<2) {foreach(var s in group)s.CombinedRaceId="";continue;}
        }
        if(_loadedSession is not null && (dialog.PendingCombinedRaces.Count>0 || _loadedSession.IsCombinedRace && _loadedSession.CombinedMembers.Any(m=>!_activeEvent.Sessions.Contains(m))))
        {
            var selected=_activeEvent.Sessions.FirstOrDefault(s=>s.Id==_loadedSession.Id);
            _loadedSession=null;
            if(selected is not null)LoadSessionIntoRaceControl(selected,saveSettings:false);
        }
    }
    private static EventSessionDefinition? MemberFor(EventSessionDefinition run,SessionEntrant entrant) =>
        run.CombinedMembers.FirstOrDefault(m=>m.Id==entrant.SourceSessionId);
    private string ColorFor(string classId) => Classes.FirstOrDefault(c=>c.Id==classId)?.PrintColor ?? "#808080";
    private double MinimumFor(EventSessionDefinition run,SessionEntrant entrant)
    {
        var member=MemberFor(run,entrant);
        return member?.MinimumLapSecondsOverride ?? Classes.FirstOrDefault(c=>c.Id==member?.ClassId)?.MinimumLapSeconds ?? 1;
    }
    private void SyncCombinedRun()
    {
        // A lineup edit can replace the original session objects. Never write a stale preview back.
        if (_loadedSession is not {IsCombinedRace:true} run || _activeEvent is null) return;
        if (run.CombinedMembers.All(m=>_activeEvent.Sessions.Contains(m) && m.CombinedRaceId==run.CombinedRaceId))
            CombinedRaces.WriteBack(run);
    }
}
