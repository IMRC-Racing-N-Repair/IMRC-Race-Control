using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IMRCRaceControl.Models;
using IMRCRaceControl.Core;

namespace IMRCRaceControl.Dialogs;
public partial class RoundMainBuilderDialog
{
    private bool _overviewEditing;
    private bool _refreshingOverview;
    private void RefreshLineupOverview()
    {
        if (HeatGrid is null) return;
        var selected=HeatGrid.SelectedItem as HeatPlan;
        _refreshingOverview=true;
        try
        {
            var list=HeatPlans.ToList();
            if (_overviewEditing)
                foreach(var plan in list.Where(h=>h.SessionType==SessionType.Qualifying && !h.SessionDisplay.StartsWith($"Q{h.Round} • ")))
                    plan.SessionDisplay=$"Q{plan.Round} • {plan.SessionDisplay}";
            if (_overviewEditing && !_browsingSavedLineups)
            {
                var round=SavedRoundFilter.SelectedItem as SavedRoundChoice;
                var cls=SavedClassFilter.SelectedItem as ClassChoice;
                var scopeClasses=SelectedClasses().Select(c=>c.Id).ToHashSet();
                foreach(var s in _raceEvent.Sessions.Where(s=>s.SessionType is SessionType.Qualifying or SessionType.Race))
                {
                    bool inScope=scopeClasses.Contains(s.ClassId) && (CurrentMode==BuildMode.Mains ? s.SessionType==SessionType.Race : s.SessionType==SessionType.Qualifying && s.Round==CurrentRound());
                    if(inScope || (cls is {AllClasses:false} && cls.ClassId!=s.ClassId) ||
                        (round is {Round:>=0} && !(round.Round==0 ? s.SessionType==SessionType.Race : s.SessionType==SessionType.Qualifying && s.Round==round.Round))) continue;
                    var plan=new HeatPlan { SourceSessionId=s.Id, ClassId=s.ClassId,ClassName=s.ClassName,Name=s.Name,
                        SessionType=s.SessionType,Round=s.Round,RaceNumber=s.RaceNumber,Order=s.QueueOrder,
                        SessionDisplay=$"{s.RoundLabel} • {s.HeatMainDisplay}",IsLocked=s.ProgressStatus is EventSessionStatus.Completed or EventSessionStatus.Running };
                    foreach(var e in s.Entrants)plan.Entrants.Add(CloneEntrant(e));
                    list.Add(plan);
                }
                list=list.OrderBy(h=>_raceEvent.Sessions.FirstOrDefault(s=>s.Id==h.SourceSessionId)?.QueueOrder ?? int.MaxValue).ThenBy(h=>h.Order).ToList();
            }
            HeatGrid.ItemsSource=list;
            HeatGrid.SelectedItem=selected is null ? null : list.FirstOrDefault(h=>ReferenceEquals(h,selected)) ?? list.FirstOrDefault(h=>h.SourceSessionId==selected.SourceSessionId && selected.SourceSessionId.Length>0);
        }
        finally { _refreshingOverview=false; }
    }
    private void HeatGrid_RightClick(object sender,MouseButtonEventArgs e)
    {
        if(e.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(HeatGrid,source) is not DataGridRow row) return;
        HeatGrid.SelectedItem=row.Item;
    }
    private bool ResolveHeatDrivers(HeatPlan source)
    {
        var targets=HeatPlans.Where(h=>h!=source && !h.IsLocked && h.ClassId==source.ClassId && h.SessionType==source.SessionType && h.Round==source.Round && (source.SessionType!=SessionType.Race || h.MainLeg==source.MainLeg)).ToList();
        var dialog=new Window {Title="Remove heat / main",Width=540,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=this,ResizeMode=ResizeMode.NoResize};
        var panel=new StackPanel {Margin=new Thickness(18)};dialog.Content=panel;
        panel.Children.Add(new TextBlock {Text=$"{source.Name} contains {source.Entrants.Count} drivers.",FontSize=17,FontWeight=FontWeights.Bold,TextWrapping=TextWrapping.Wrap});
        var remove=new RadioButton {Content="Remove all drivers from this heat",IsChecked=true,Margin=new Thickness(0,16,0,8)};
        var move=new RadioButton {Content="Move all drivers to another race",IsEnabled=targets.Count>0};
        var picker=new ComboBox {ItemsSource=targets,DisplayMemberPath="Name",SelectedIndex=targets.Count>0?0:-1,Margin=new Thickness(0,8,0,8)};
        panel.Children.Add(remove); panel.Children.Add(move);panel.Children.Add(picker);
        panel.Children.Add(new TextBlock {Text="Drivers stay registered in the event. Save lineup changes and reprint after removing the heat.",TextWrapping=TextWrapping.Wrap});
        var error=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};panel.Children.Add(error);
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);
        var cancel=new Button {Content="CANCEL",IsCancel=true};var apply=new Button {Content="REMOVE HEAT"};buttons.Children.Add(cancel);buttons.Children.Add(apply);
        apply.Click+=(_,_)=>
        {
            if(move.IsChecked==true)
            {
                if(picker.SelectedItem is not HeatPlan target)return;
                try { HeatDriverTransfer.Validate(source.Entrants.ToList(),target.Entrants.ToList(),target.SessionType==SessionType.Race && target.MainCapacity>0 ? Math.Min(30,target.MainCapacity):30); }
                catch(InvalidOperationException ex){error.Text=ex.Message;return;}
                foreach(var driver in source.Entrants)target.Entrants.Add(driver);
                Renumber(target.Entrants);
            }
            source.Entrants.Clear();dialog.DialogResult=true;
        };
        return dialog.ShowDialog()==true;
    }
}

public partial class RoundMainBuilderDialog
{
    internal static void ExportOverviewPreview(string output)
    {
        var classes=new[]{new ClassDefinition {Name="Slash Pro Stock"},new ClassDefinition {Name="25.5 Foam Tire Truck"}};
        var ev=new RaceDayEventDefinition {Name="Lineup editing checks",ClassIds=classes.Select(c=>c.Id).ToList()};
        foreach(var cls in classes)
            foreach(var round in new[]{1,2})
                for(int heat=1;heat<=(round==1?2:1);heat++)ev.Sessions.Add(new EventSessionDefinition { ClassId=cls.Id,ClassName=cls.Name,Round=round,Heat=heat,QueueOrder=ev.Sessions.Count+1,RaceNumber=heat,
                    Entrants=[new SessionEntrant {DriverId=Guid.NewGuid().ToString(),DriverName="Sample Racer",CarNumber=1}] });
        var dialog=new RoundMainBuilderDialog(ev,classes,[]);dialog.Show();dialog.UpdateLayout();
        dialog.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if(dialog.HeatGrid.Items.Count!=6)throw new InvalidOperationException("Saved lineup overview did not show all six races.");
        var first=dialog.HeatGrid.Items.Cast<HeatPlan>().First();dialog.HeatGrid.SelectedItem=first;
        if(dialog.HeatGrid.Items.Count!=6 || dialog._browsingSavedLineups || dialog.HeatPlans.Count!=2)throw new InvalidOperationException($"Selecting a race narrowed the overview or failed to open its roster: visible={dialog.HeatGrid.Items.Count}, browsing={dialog._browsingSavedLineups}, editing={dialog.HeatPlans.Count}.");
        var other=dialog.HeatGrid.Items.Cast<HeatPlan>().First(h=>h.ClassId==classes[1].Id && h.Round==2);dialog.HeatGrid.SelectedItem=other;
        if(dialog.HeatGrid.Items.Count!=6 || dialog.HeatPlans.Count!=1 || dialog.HeatPlans[0].Round!=2)throw new InvalidOperationException("Changing class/round hid the lineup.");
        dialog.AddHeat_Click(dialog,new RoutedEventArgs());
        if(dialog.HeatGrid.Items.Count!=7 || dialog.HeatPlans.Count!=2 || dialog.HeatPlans.Any(h=>h.ClassId!=classes[1].Id || h.Round!=2))throw new InvalidOperationException("Add heat used the wrong class/round.");
        dialog.RemoveHeat_Click(dialog,new RoutedEventArgs());
        if(dialog.HeatGrid.Items.Count!=6 || ev.Sessions.Count!=6 || dialog.HeatPlans.Any(h=>h.ClassId!=classes[1].Id || h.Round!=2))throw new InvalidOperationException("Removing preview heat changed saved sessions or overview.");
        dialog.UpdateLayout();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth),(int)Math.Ceiling(dialog.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(dialog);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using(var file=System.IO.File.Create(System.IO.Path.Combine(output,"Lineup-Overview.png")))encoder.Save(file);
        dialog._lineupSnapshot=null;dialog.Close();
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"Lineup-Overview-Checks.txt"),"PASS: all classes/rounds remain visible while selecting, editing, adding and removing a heat; source sessions unchanged before saving.");
    }
}
