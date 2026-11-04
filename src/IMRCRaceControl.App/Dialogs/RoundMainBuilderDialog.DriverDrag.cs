using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
namespace IMRCRaceControl.Dialogs;
public partial class RoundMainBuilderDialog
{
    private sealed record DriverDrag(HeatPlan Source,SessionEntrant Driver);
    private Point _driverDragOrigin;
    private DriverDrag? _draggedDriver;
    private void DriverDragStart(object sender,MouseButtonEventArgs e)
    {
        _draggedDriver=null;_driverDragOrigin=e.GetPosition(EntrantGrid);
        if(_browsingSavedLineups || HeatGrid.SelectedItem is not HeatPlan {IsLocked:false} source)return;
        if(e.OriginalSource is DependencyObject node && ItemsControl.ContainerFromElement(EntrantGrid,node) is DataGridRow {Item:SessionEntrant driver})
            _draggedDriver=new(source,driver);
    }
    private void DriverDragMove(object sender,MouseEventArgs e)
    {
        if(e.LeftButton!=MouseButtonState.Pressed || _draggedDriver is not DriverDrag drag)return;
        var point=e.GetPosition(EntrantGrid);
        if(Math.Abs(point.X-_driverDragOrigin.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y-_driverDragOrigin.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        _draggedDriver=null;
        if(_browsingSavedLineups || drag.Source.IsLocked || !drag.Source.Entrants.Contains(drag.Driver))return;
        DragDrop.DoDragDrop(EntrantGrid,new DataObject("IMRC.LineupDriver",drag),DragDropEffects.Move);
    }
    private string? DriverMoveError(HeatPlan source,HeatPlan? target,SessionEntrant driver)
    {
        if(target is null || target==source)return "Drop the driver onto another race in the lineup.";
        if(_browsingSavedLineups || source.IsLocked || target.IsLocked)return "Completed races are locked. Restart the race before changing its lineup.";
        if(!HeatPlans.Contains(source) || !HeatPlans.Contains(target) || source.ClassId!=target.ClassId || source.SessionType!=target.SessionType || source.Round!=target.Round || source.SessionType==SessionType.Race && source.MainLeg!=target.MainLeg)
            return "Drivers can only move between races in the same class and round.";
        if(!source.Entrants.Contains(driver))return "This driver is no longer in the source race.";
        try { HeatDriverTransfer.Validate([driver],target.Entrants.ToList(),target.SessionType==SessionType.Race && target.MainCapacity>0?Math.Min(30,target.MainCapacity):30); }
        catch(InvalidOperationException ex){return ex.Message;}
        return null;
    }
    private bool MoveLineupDriver(HeatPlan source,HeatPlan target,SessionEntrant driver)
    {
        if(DriverMoveError(source,target,driver) is string error){BuilderStatusText.Text=error;return false;}
        source.Entrants.Remove(driver);target.Entrants.Add(driver);
        Renumber(source.Entrants);Renumber(target.Entrants);
        HeatGrid.Items.Refresh();EntrantGrid.Items.Refresh();RefreshTargetHeats();
        SaveButton.IsEnabled=true;
        BuilderStatusText.Text=$"{driver.DisplayName} moved to {target.Name}. SAVE CHANGES to keep the lineup.";
        return true;
    }
    private bool HandleDriverDragOver(DragEventArgs e)
    {
        if(e.Data.GetData("IMRC.LineupDriver") is not DriverDrag drag)return false;
        e.Effects=DriverMoveError(drag.Source,RaceAt(e.OriginalSource),drag.Driver) is null?DragDropEffects.Move:DragDropEffects.None;
        e.Handled=true;return true;
    }
    private bool HandleDriverDrop(DragEventArgs e)
    {
        if(e.Data.GetData("IMRC.LineupDriver") is not DriverDrag drag)return false;
        var target=RaceAt(e.OriginalSource);
        if(target is not null)MoveLineupDriver(drag.Source,target,drag.Driver);
        else BuilderStatusText.Text="Drop the driver onto a race row in the lineup.";
        e.Handled=true;return true;
    }
}
