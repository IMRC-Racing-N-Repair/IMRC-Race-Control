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
        _raceDragPlan = null;
        _raceDragId = null;
        // Ordinary row clicks select only. Dragging starts on the MOVE column.
        if (e.OriginalSource is not DependencyObject node) return;
        var cell = FindDragCell(node);
        if (cell?.Column.Header is not string header || header != "MOVE") return;
        _raceDragOrigin = e.GetPosition(HeatGrid);
        _raceDragPlan = RaceAt(e.OriginalSource);
        _raceDragId = _raceDragPlan?.SourceSessionId;
    }

    private static DataGridCell? FindDragCell(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is DataGridCell cell) return cell;
            node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    private void RaceDragEnd(object sender, MouseButtonEventArgs e)
    {
        _raceDragPlan = null;
        _raceDragId = null;
    }

    private void RaceDragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        { _raceDragPlan=null; _raceDragId=null; return; }
        if (_holdingCombine || _raceDragPlan is null) return;
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
        if (string.IsNullOrEmpty(id) && !_browsingSavedLineups && HeatPlans.Contains(plan) && !plan.IsLocked &&
            !plan.IsCombinedHeader && !plan.IsCombinedChild)
        {
            DragWithVisual(HeatGrid, new DataObject("IMRC.PreviewHeat", plan), $"{plan.ClassName} · {plan.SessionDisplay}", $"{plan.RacerCount} racers · Move race");
            return;
        }
        var source = PreviewSessions.FirstOrDefault(s => s.Id == id);
        if (source is not null && RaceLineupOrder.Movable(source) && !plan.IsCombinedHeader && !plan.IsCombinedChild)
            DragWithVisual(HeatGrid, new DataObject("IMRC.LineupRace", id!), $"Race {plan.RaceNumberDisplay} · {plan.ClassName}", $"{plan.SessionDisplay} · {plan.RacerCount} racers");
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
        return sourceSession is not null && targetSession is not null && target is {IsCombinedHeader:false,IsCombinedChild:false} &&
               RaceLineupOrder.CanMove(sourceSession, targetSession);
    }

    private void RaceDragOver(object sender, DragEventArgs e)
    {
        if (HandleDriverDragOver(e)) return;
        e.Effects = CanDropPreviewHeat(e, out _, out _) || CanDropRace(e, out _, out _)
            ? DragDropEffects.Move : DragDropEffects.None;
        ShowDropTarget(e, e.Effects == DragDropEffects.Move, false);
        e.Handled = true;
    }

    private void RaceDrop(object sender, DragEventArgs e)
    {
        if (HandleDriverDrop(e)) return;
        if (CanDropPreviewHeat(e, out var plan, out var destination))
        {
            MovePreviewHeat(plan!, destination!, DropAfter(e));
        }
        else if (CanDropRace(e, out var id, out var target))
        {
            StageRaceMove(id, target!.SourceSessionId, DropAfter(e));
        }
        else
        {
            BuilderStatusText.Text = "Drag unrun heats within the same qualifying round or move unrun mains independently. Completed races stay locked.";
        }
        e.Handled = true;
    }

    private void MovePreviewHeat(HeatPlan source, HeatPlan target, bool? after = null)
    {
        if (!HeatPlans.Contains(source) || !HeatPlans.Contains(target) ||
            !CanReorderPreviewHeat(source, target)) return;
        var from = HeatPlans.IndexOf(source);
        var to = HeatPlans.IndexOf(target);
        if (after.HasValue)
        {
            to += after.Value ? 1 : 0;
            if (from < to) to--;
        }
        if (from == to) return;
        HeatPlans.Move(from, to);
        ReorderNumbers();
        HeatGrid.SelectedItem = source;
        SaveButton.IsEnabled = true;
        BuilderStatusText.Text = "Heat moved. The preview is renumbered top to bottom. Press SAVE CHANGES to keep this lineup.";
    }

    private void StageRaceMove(string sourceId,string targetId,bool after = false)
    {
        var source=PreviewSessions.FirstOrDefault(s=>s.Id==sourceId);
        var target=PreviewSessions.FirstOrDefault(s=>s.Id==targetId);
        if(source is null || target is null || !RaceLineupOrder.CanMove(source,target) ||
           GroupFor(source.Id).Length>0 || GroupFor(target.Id).Length>0)return;
        var group=PreviewSessions.Where(s=>RaceLineupOrder.GroupKey(s)==RaceLineupOrder.GroupKey(source) && RaceLineupOrder.Movable(s))
            .OrderBy(s=>_pendingRaceOrder.GetValueOrDefault(s.Id,s.LineupOrder??int.MaxValue)).ThenBy(s=>s.QueueOrder).ToList();
        var moving=group.Where(s=>s.Id==sourceId).ToList();
        if(moving.Contains(target))return;
        group.RemoveAll(moving.Contains);
        var to=group.FindIndex(s=>s.Id==targetId);
        group.InsertRange(to+(after?1:0),moving);
        for(int i=0;i<group.Count;i++)_pendingRaceOrder[group[i].Id]=i+1;
        RefreshLineupOverview();SaveButton.IsEnabled=true;
        BuilderStatusText.Text="Race moved independently. SAVE CHANGES to keep the new position; completed races stay locked.";
    }
    private List<HeatPlan> OrderOverview(List<HeatPlan> plans)
    {
        // Apply order to the entire saved queue before filtering or swapping
        // the edited roster. Selecting another class cannot reorder races.
        var sessions=PreviewSessions.Select(s=>new EventSessionDefinition
        {
            Id=s.Id,SessionType=s.SessionType,Round=s.Round,MainLeg=s.MainLeg,
            QueueOrder=s.QueueOrder,ProgressStatus=s.ProgressStatus,
            LineupOrder=_pendingRaceOrder.TryGetValue(s.Id,out var order)?order:s.LineupOrder
        }).ToList();
        RaceLineupOrder.Apply(sessions);
        var queue=sessions.ToDictionary(s=>s.Id,s=>s.QueueOrder,StringComparer.OrdinalIgnoreCase);
        return plans.Select((p,i)=>(Plan:p,Index:i))
            .OrderBy(x=>x.Plan.SessionType==SessionType.Qualifying?0:x.Plan.SessionType==SessionType.Race?1:2)
            .ThenBy(x=>x.Plan.SessionType==SessionType.Race?0:x.Plan.Round)
            .ThenBy(x=>queue.TryGetValue(x.Plan.SourceSessionId,out var q)&&q>0?q:int.MaxValue)
            .ThenBy(x=>x.Plan.Order).ThenBy(x=>x.Index).Select(x=>x.Plan).ToList();
    }
}
