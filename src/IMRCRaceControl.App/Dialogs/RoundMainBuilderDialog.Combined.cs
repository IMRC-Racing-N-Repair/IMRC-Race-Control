using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
namespace IMRCRaceControl.Dialogs;
public partial class RoundMainBuilderDialog
{
    private readonly Dictionary<string,string> _pendingCombinedRaces = new();
    public IReadOnlyDictionary<string,string> PendingCombinedRaces => _pendingCombinedRaces;
    private readonly HashSet<string> _combineSelection = new();
    private bool _holdingCombine;
    private bool HasPendingSchedule => _pendingRaceOrder.Count>0 || _pendingCombinedRaces.Count>0;
    private string GroupFor(string id) => _pendingCombinedRaces.TryGetValue(id,out var group) ? group : PreviewSessions.FirstOrDefault(s=>s.Id==id)?.CombinedRaceId ?? "";

    private void CombineKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key is not (Key.LeftShift or Key.RightShift) || _holdingCombine) return;
        if(Keyboard.FocusedElement is TextBox)return;
        BeginCombineSelection();
    }
    private void BeginCombineSelection()
    {
        _holdingCombine=true; _combineSelection.Clear(); HeatGrid.Cursor=Cursors.Hand;
        _refreshingOverview=true; try {HeatGrid.SelectedItems.Clear();} finally {_refreshingOverview=false;}
    }
    private void CancelCombineSelection()
    {
        if(!_holdingCombine)return;
        _holdingCombine=false; HeatGrid.Cursor=null; _combineSelection.Clear();
        _refreshingOverview=true; try {HeatGrid.SelectedItems.Clear();} finally {_refreshingOverview=false;}
    }
    private void CombineKeyUp(object sender,KeyEventArgs e)
    {
        if(e.Key is not (Key.LeftShift or Key.RightShift) || !_holdingCombine || (Keyboard.Modifiers & ModifierKeys.Shift)!=0)return;
        var ids=_combineSelection.ToList(); CancelCombineSelection();
        if(ids.Count<2) {UpdateSelectedLineup(); return;}
        ConfirmCombine(ids);
    }
    private bool CombineClick(MouseButtonEventArgs e)
    {
        if((Keyboard.Modifiers & ModifierKeys.Shift)==0)return false;
        if(!_holdingCombine)BeginCombineSelection();
        var row=RaceAt(e.OriginalSource);
        e.Handled=true;
        if(row is not null)ToggleCombineRace(row);
        return true;
    }
    private void ToggleCombineRace(HeatPlan row)
    {
        if(row.IsCombinedHeader)return;
        if(row.SourceSessionId.Length==0 || row.IsLocked)
        {BuilderStatusText.Text="Select saved, unrun races to combine. Save new lineups first; restart completed races before editing.";return;}
        _refreshingOverview=true;
        try
        {
            if(_combineSelection.Add(row.SourceSessionId))HeatGrid.SelectedItems.Add(row);
            else {_combineSelection.Remove(row.SourceSessionId);HeatGrid.SelectedItems.Remove(row);}
        }
        finally {_refreshingOverview=false;}
        BuilderStatusText.Text=$"{_combineSelection.Count} races selected. Release Shift to combine.";
    }
    private List<EventSessionDefinition> ExpandSelection(IEnumerable<string> ids)
    {
        var selected=ids.ToHashSet();
        var groups=selected.Select(GroupFor).Where(g=>g.Length>0).ToHashSet();
        return PreviewSessions.Where(s=>selected.Contains(s.Id) || groups.Contains(GroupFor(s.Id)))
            .OrderBy(s=>s.QueueOrder).ToList();
    }
    private void ConfirmCombine(IEnumerable<string> ids)
    {
        var members=ExpandSelection(ids);
        try {CombinedRaces.Validate(members,_classes);} catch(InvalidOperationException ex)
        {MessageBox.Show(this,ex.Message,"Combine races",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        var dialog=new Window {Title="Combine races",Owner=this,Width=560,SizeToContent=SizeToContent.Height,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var panel=new StackPanel {Margin=new Thickness(20)};dialog.Content=panel;
        panel.Children.Add(new TextBlock {Text="COMBINE THESE RACES?",FontSize=20,FontWeight=FontWeights.Bold});
        foreach(var member in members)panel.Children.Add(new TextBlock {Text=$"• {member.ClassName} — {member.RoundLabel}, {member.HeatMainDisplay} • {member.RacerCount} drivers",Margin=new Thickness(0,8,0,0),TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock {Text="One start and one on-track run. Each class/heat keeps its own positions, lap target, results and qualifying history. Changes take effect when you SAVE CHANGES.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,16)});
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);
        var cancel=new Button {Content="CANCEL",IsCancel=true}; var combine=new Button {Content="COMBINE RACES",IsDefault=true};
        buttons.Children.Add(cancel);buttons.Children.Add(combine);combine.Click+=(_,_)=>dialog.DialogResult=true;
        if(dialog.ShowDialog()!=true)return;
        StageCombination(members);
    }
    private void StageCombination(List<EventSessionDefinition> members)
    {
        CombinedRaces.Validate(members,_classes);
        var group=Guid.NewGuid().ToString("N");
        foreach(var m in members)_pendingCombinedRaces[m.Id]=group;
        RefreshLineupOverview();SaveButton.IsEnabled=true;
        BuilderStatusText.Text="Combined race preview created. SAVE CHANGES to keep it. Classes remain scored separately.";
    }
    private void SeparateCombined_Click(object sender,RoutedEventArgs e)
    {
        if(HeatGrid.SelectedItem is not HeatPlan selected)return;
        var group=selected.IsCombinedHeader ? selected.CombinationId : GroupFor(selected.SourceSessionId);
        if(group.Length==0)return;
        var members=PreviewSessions.Where(s=>GroupFor(s.Id)==group).ToList();
        if(members.Any(s=>!RaceLineupOrder.Movable(s))) {MessageBox.Show(this,"Restart the combined race before separating completed or running heats.","Separate races");return;}
        foreach(var m in members)_pendingCombinedRaces[m.Id]="";
        RefreshLineupOverview();SaveButton.IsEnabled=true;
        BuilderStatusText.Text="Races separated in this preview. SAVE CHANGES to keep them separate.";
    }
    private List<HeatPlan> GroupCombinedOverview(List<HeatPlan> plans)
    {
        var result=new List<HeatPlan>();var seen=new HashSet<string>();
        foreach(var plan in plans)
        {
            var group=GroupFor(plan.SourceSessionId);
            plan.CombinationId=group;plan.IsCombinedChild=group.Length>0;
            if(group.Length==0){result.Add(plan);continue;}
            var allMembers=PreviewSessions.Where(s=>GroupFor(s.Id)==group)
                .OrderBy(s=>_pendingCombinedRaces.ContainsKey(s.Id) ? 0 : s.CombinedRaceOrder)
                .ThenBy(s=>s.QueueOrder).ToList();
            if(allMembers.Count<2){plan.IsCombinedChild=false;result.Add(plan);continue;}
            if(!seen.Add(group))continue;
            var children=plans.Where(p=>GroupFor(p.SourceSessionId)==group).ToList();
            var lead=allMembers[0];
            result.Add(new HeatPlan {IsCombinedHeader=true,IsLocked=true,CombinationId=group,
                RaceNumber=lead.RaceNumber,
                CombinedChildren=children,
                HiddenCombinedRacerCount=allMembers.Where(s=>children.All(p=>p.SourceSessionId!=s.Id)).Sum(s=>s.RacerCount),
                ClassName="Combined Classes",SessionDisplay=$"One race • {allMembers.Count} class heats",
                SessionType=plan.SessionType,Round=plan.Round,MainLeg=plan.MainLeg,StatusDisplay="COMBINED"});
            result.AddRange(children);
        }
        return result;
    }
}
