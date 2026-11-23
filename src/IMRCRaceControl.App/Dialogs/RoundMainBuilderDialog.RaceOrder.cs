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
    private HeatPlan? _raceDragPlan;
    private HeatPlan? RaceAt(object source) =>
        source is DependencyObject node && ItemsControl.ContainerFromElement(HeatGrid, node) is DataGridRow row
            ? row.Item as HeatPlan : null;

    private static bool CanReorderPreviewHeat(HeatPlan source, HeatPlan target) =>
        !ReferenceEquals(source, target) &&
        !source.IsLocked && !target.IsLocked &&
        !source.IsCombinedHeader && !target.IsCombinedHeader &&
        !source.IsCombinedChild && !target.IsCombinedChild &&
        source.SessionType == target.SessionType &&
        (source.SessionType == SessionType.Race || source.Round == target.Round);

    private void RaceDragStart(object sender, MouseButtonEventArgs e)
    {
        if (CombineClick(e))
        {
            _raceDragPlan = null;
            _raceDragId = null;
            return;
        }
        _raceDragOrigin = e.GetPosition(HeatGrid);
        _raceDragPlan = RaceAt(e.OriginalSource);
        _raceDragId = _raceDragPlan?.SourceSessionId;
    }

    private void RaceDragMove(object sender, MouseEventArgs e)
    {
        if (_holdingCombine || e.LeftButton != MouseButtonState.Pressed || _raceDragPlan is null)
            return;
        var point = e.GetPosition(HeatGrid);
        if (Math.Abs(point.X - _raceDragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _raceDragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var plan = _raceDragPlan;
        var id = _raceDragId;
        _raceDragPlan = null;
        _raceDragId = null;

        // Newly generated heats have no SourceSessionId. They still must be
        // draggable before saving, and an edited saved heat should move its
        // current preview (not silently modify the persistent event).
        if (!_browsingSavedLineups && HeatPlans.Contains(plan) && !plan.IsLocked &&
            !plan.IsCombinedHeader && !plan.IsCombinedChild)
        {
            DragDrop.DoDragDrop(HeatGrid, new DataObject("IMRC.PreviewHeat", plan), DragDropEffects.Move);
            return;
        }
        var source = PreviewSessions.FirstOrDefault(s => s.Id == id);
        if (source is not null && RaceLineupOrder.Movable(source))
            DragDrop.DoDragDrop(HeatGrid, new DataObject("IMRC.LineupRace", id!), DragDropEffects.Move);
    }

    private bool CanDropPreviewHeat(DragEventArgs e, out HeatPlan? source, out HeatPlan? target)
    {
        source = e.Data.GetData("IMRC.PreviewHeat") as HeatPlan;
        target = RaceAt(e.OriginalSource);
        return !_browsingSavedLineups &&
               source is not null && target is not null &&
               HeatPlans.Contains(source) && HeatPlans.Contains(target) &&
               CanReorderPreviewHeat(source, target);
    }

    private bool CanDropRace(DragEventArgs e, out string id, out HeatPlan? target)
    {
        id = e.Data.GetData("IMRC.LineupRace") as string ?? "";
        target = RaceAt(e.OriginalSource);
        var sourceId = id;
        var targetId = target?.SourceSessionId;
        var sourceSession = PreviewSessions.FirstOrDefault(s => s.Id == sourceId);
        var targetSession = PreviewSessions.FirstOrDefault(s => s.Id == targetId);
        return sourceSession is not null && targetSession is not null &&
               RaceLineupOrder.CanMove(sourceSession, targetSession);
    }

    private void RaceDragOver(object sender, DragEventArgs e)
    {
        if (HandleDriverDragOver(e)) return;
        e.Effects = CanDropPreviewHeat(e, out _, out _) || CanDropRace(e, out _, out _)
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void RaceDrop(object sender, DragEventArgs e)
    {
        if (HandleDriverDrop(e)) return;
        if (CanDropPreviewHeat(e, out var plan, out var destination))
        {
            MovePreviewHeat(plan!, destination!);
        }
        else if (CanDropRace(e, out var id, out var target))
        {
            StageRaceMove(id, target!.SourceSessionId);
        }
        else
        {
            BuilderStatusText.Text = "Drag unrun heats within the same qualifying round or move unrun mains independently. Completed races stay locked.";
        }
        e.Handled = true;
    }

    private void MovePreviewHeat(HeatPlan source, HeatPlan target)
    {
        if (!HeatPlans.Contains(source) || !HeatPlans.Contains(target) ||
            !CanReorderPreviewHeat(source, target)) return;
        HeatPlans.Move(HeatPlans.IndexOf(source), HeatPlans.IndexOf(target));
        ReorderNumbers();
        HeatGrid.SelectedItem = source;
        SaveButton.IsEnabled = true;
        BuilderStatusText.Text = "Heat moved. The preview is renumbered top to bottom. Press SAVE CHANGES to keep this lineup.";
    }

    private void StageRaceMove(string sourceId,string targetId)
    {
        var source=PreviewSessions.Single(s=>s.Id==sourceId);var target=PreviewSessions.Single(s=>s.Id==targetId);
        if(!RaceLineupOrder.CanMove(source,target))return;
        var group=PreviewSessions.Where(s=>RaceLineupOrder.GroupKey(s)==RaceLineupOrder.GroupKey(source) && RaceLineupOrder.Movable(s))
            .OrderBy(s=>_pendingRaceOrder.GetValueOrDefault(s.Id,s.LineupOrder??int.MaxValue)).ThenBy(s=>s.QueueOrder).ToList();
        var moving=group.Where(s=>s.Id==sourceId).ToList();
        if(moving.Contains(target))return;
        group.RemoveAll(moving.Contains);
        var to=group.FindIndex(s=>s.Id==targetId);
        group.InsertRange(to,moving);
        for(int i=0;i<group.Count;i++)_pendingRaceOrder[group[i].Id]=i+1;
        RefreshLineupOverview();SaveButton.IsEnabled=true;
        BuilderStatusText.Text="Race moved independently. SAVE CHANGES to keep the new position; completed races stay locked.";
    }
    private List<HeatPlan> OrderOverview(List<HeatPlan> plans)
    {
        var rows=plans.Select((p,i)=>(Plan:p,Session:new EventSessionDefinition {Id=p.SourceSessionId,SessionType=p.SessionType,Round=p.Round,MainLeg=p.MainLeg,
            QueueOrder=PreviewSessions.FirstOrDefault(s=>s.Id==p.SourceSessionId)?.QueueOrder??100000+i,
            ProgressStatus=PreviewSessions.FirstOrDefault(s=>s.Id==p.SourceSessionId)?.ProgressStatus??EventSessionStatus.Planned,
            LineupOrder=_pendingRaceOrder.TryGetValue(p.SourceSessionId,out var order)?order:PreviewSessions.FirstOrDefault(s=>s.Id==p.SourceSessionId)?.LineupOrder})).ToList();
        RaceLineupOrder.Apply(rows.Select(r=>r.Session));
        return rows.OrderBy(r=>r.Session.ScheduleStageOrder).ThenBy(r=>r.Session.SessionType==SessionType.Race ? 0 : r.Session.ScheduleRoundOrder).ThenBy(r=>r.Session.QueueOrder).Select(r=>r.Plan).ToList();
    }
}
