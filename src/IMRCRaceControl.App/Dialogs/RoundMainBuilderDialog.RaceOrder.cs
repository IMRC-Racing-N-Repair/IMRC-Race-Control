using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
namespace IMRCRaceControl.Dialogs;
public partial class RoundMainBuilderDialog
{
    private readonly Dictionary<string,int> _pendingRaceOrder=new();
    public IReadOnlyDictionary<string,int> PendingRaceOrder=>_pendingRaceOrder;
    public bool OrderOnlySave {get;private set;}
    private Point _raceDragOrigin;
    private string? _raceDragId;
    private HeatPlan? RaceAt(object source)=>source is DependencyObject node && ItemsControl.ContainerFromElement(HeatGrid,node) is DataGridRow row ? row.Item as HeatPlan:null;
    private void RaceDragStart(object sender,MouseButtonEventArgs e)
    {
        _raceDragOrigin=e.GetPosition(HeatGrid);_raceDragId=RaceAt(e.OriginalSource)?.SourceSessionId;
    }
    private void RaceDragMove(object sender,MouseEventArgs e)
    {
        if(e.LeftButton!=MouseButtonState.Pressed || string.IsNullOrEmpty(_raceDragId))return;
        var point=e.GetPosition(HeatGrid);
        if(Math.Abs(point.X-_raceDragOrigin.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y-_raceDragOrigin.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        var source=_raceEvent.Sessions.FirstOrDefault(s=>s.Id==_raceDragId);
        var id=_raceDragId;_raceDragId=null;
        if(source is not null && RaceLineupOrder.Movable(source))DragDrop.DoDragDrop(HeatGrid,new DataObject("IMRC.LineupRace",id),DragDropEffects.Move);
    }
    private bool CanDropRace(DragEventArgs e,out string id,out HeatPlan? target)
    {
        id=e.Data.GetData("IMRC.LineupRace") as string ?? "";target=RaceAt(e.OriginalSource);
        var sourceId=id;
        var sourceSession=_raceEvent.Sessions.FirstOrDefault(s=>s.Id==sourceId);
        var targetId=target?.SourceSessionId;
        var targetSession=_raceEvent.Sessions.FirstOrDefault(s=>s.Id==targetId);
        return sourceSession is not null && targetSession is not null && RaceLineupOrder.CanMove(sourceSession,targetSession);
    }
    private void RaceDragOver(object sender,DragEventArgs e)
    {
        if(HandleDriverDragOver(e))return;
        e.Effects=CanDropRace(e,out _,out _)?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;
    }
    private void RaceDrop(object sender,DragEventArgs e)
    {
        if(HandleDriverDrop(e))return;
        if(CanDropRace(e,out var id,out var target))StageRaceMove(id,target!.SourceSessionId);
        else BuilderStatusText.Text="Move unrun races within the same round only. Completed races stay locked.";
        e.Handled=true;
    }
    private void StageRaceMove(string sourceId,string targetId)
    {
        var source=_raceEvent.Sessions.Single(s=>s.Id==sourceId);var target=_raceEvent.Sessions.Single(s=>s.Id==targetId);
        if(!RaceLineupOrder.CanMove(source,target))return;
        var group=_raceEvent.Sessions.Where(s=>DashboardRoundChoice.From(s)==DashboardRoundChoice.From(source) && RaceLineupOrder.Movable(s))
            .OrderBy(s=>_pendingRaceOrder.GetValueOrDefault(s.Id,s.LineupOrder??int.MaxValue)).ThenBy(s=>s.QueueOrder).ToList();
        var from=group.IndexOf(source);var to=group.IndexOf(target);group.RemoveAt(from);group.Insert(to,source);
        for(int i=0;i<group.Count;i++)_pendingRaceOrder[group[i].Id]=i+1;
        RefreshLineupOverview();SaveButton.IsEnabled=true;
        BuilderStatusText.Text="Race order updated in this round. SAVE CHANGES to keep it. Heat numbers and drivers are unchanged.";
    }
    private List<HeatPlan> OrderOverview(List<HeatPlan> plans)
    {
        var rows=plans.Select((p,i)=>(Plan:p,Session:new EventSessionDefinition {Id=p.SourceSessionId,SessionType=p.SessionType,Round=p.Round,MainLeg=p.MainLeg,
            QueueOrder=_raceEvent.Sessions.FirstOrDefault(s=>s.Id==p.SourceSessionId)?.QueueOrder??100000+i,
            ProgressStatus=_raceEvent.Sessions.FirstOrDefault(s=>s.Id==p.SourceSessionId)?.ProgressStatus??EventSessionStatus.Planned,
            LineupOrder=_pendingRaceOrder.TryGetValue(p.SourceSessionId,out var order)?order:_raceEvent.Sessions.FirstOrDefault(s=>s.Id==p.SourceSessionId)?.LineupOrder})).ToList();
        RaceLineupOrder.Apply(rows.Select(r=>r.Session));
        return rows.OrderBy(r=>r.Session.ScheduleStageOrder).ThenBy(r=>r.Session.ScheduleRoundOrder).ThenBy(r=>r.Session.QueueOrder).Select(r=>r.Plan).ToList();
    }
}
