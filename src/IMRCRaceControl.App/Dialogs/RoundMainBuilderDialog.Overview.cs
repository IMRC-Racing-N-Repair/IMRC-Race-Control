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
            if (_overviewEditing && !_browsingSavedLineups)
            {
                var round=SavedRoundFilter.SelectedItem as SavedRoundChoice;
                var cls=SavedClassFilter.SelectedItem as ClassChoice;
                var scopeClasses=SelectedClasses().Select(c=>c.Id).ToHashSet();
                foreach(var s in PreviewSessions.Where(s=>s.SessionType is SessionType.Qualifying or SessionType.Race))
                {
                    bool inScope=scopeClasses.Contains(s.ClassId) && (CurrentMode==BuildMode.Mains ? s.SessionType==SessionType.Race : s.SessionType==SessionType.Qualifying && s.Round==CurrentRound());
                    if(inScope || (round?.MainLeg is not null && s.MainLeg!=round.MainLeg) || (cls is {AllClasses:false} && cls.ClassId!=s.ClassId) ||
                        (round is {Round:>=0} && !(round.Round==0 ? s.SessionType==SessionType.Race : s.SessionType==SessionType.Qualifying && s.Round==round.Round))) continue;
                    var plan=new HeatPlan { SourceSessionId=s.Id, ClassId=s.ClassId,ClassName=s.ClassName,Name=s.Name,
                        SessionType=s.SessionType,Round=s.Round,MainLeg=s.MainLeg,RaceNumber=s.RaceNumber,Order=s.QueueOrder,
                        SessionDisplay=$"{s.RoundLabel} • {s.HeatMainDisplay}",StatusDisplay=s.ProgressStatus.ToString().ToUpperInvariant(),IsLocked=s.ProgressStatus is EventSessionStatus.Completed or EventSessionStatus.Running };
                    foreach(var e in s.Entrants)plan.Entrants.Add(CloneEntrant(e));
                    list.Add(plan);
                }
                list=list.OrderBy(h=>PreviewSessions.FirstOrDefault(s=>s.Id==h.SourceSessionId)?.QueueOrder ?? int.MaxValue).ThenBy(h=>h.Order).ToList();
            }
            foreach(var plan in list.Where(h=>h.SessionType==SessionType.Qualifying))
            {
                var saved=PreviewSessions.FirstOrDefault(s=>s.Id==plan.SourceSessionId);
                var heat=HeatPlans.Contains(plan) ? GetHeatIndex(plan) : saved?.Heat ?? 1;
                var total=list.Count(h=>h.ClassId==plan.ClassId && h.Round==plan.Round && h.SessionType==SessionType.Qualifying);
                plan.SessionDisplay=$"Q{plan.Round} • Heat {heat}/{total}";
            }
            if (_overviewEditing && SavedRoundFilter.SelectedItem is SavedRoundChoice {MainLeg:int leg})list=list.Where(p=>p.SessionType==SessionType.Race && p.MainLeg==leg).ToList();
            list=GroupCombinedOverview(OrderOverview(list));
            var view=new System.Windows.Data.ListCollectionView(list);
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(HeatPlan.RoundGroup)));
            HeatGrid.ItemsSource=view;
            HeatGrid.SelectedItem=selected is null ? null : list.FirstOrDefault(h=>ReferenceEquals(h,selected)) ?? list.FirstOrDefault(h=>h.SourceSessionId==selected.SourceSessionId && selected.SourceSessionId.Length>0);
        }
        finally { _refreshingOverview=false; }
        UpdateSelectedLineup();
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
        ev.Sessions[0].ProgressStatus=EventSessionStatus.Completed;
        var originalCompleted=System.Text.Json.JsonSerializer.Serialize(ev.Sessions[0]);
        var dialog=new RoundMainBuilderDialog(ev,classes,[]);dialog.Show();dialog.UpdateLayout();
        dialog.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if(dialog.HeatGrid.Items.Count!=6)throw new InvalidOperationException("Saved lineup overview did not show all six races.");
        var first=dialog.HeatGrid.Items.Cast<HeatPlan>().First();var label=first.SessionDisplay;dialog.HeatGrid.SelectedItem=first;
        if(dialog.EntrantGrid.Items.Count!=1 || dialog.SelectedHeatTitle.Text=="SELECT A RACE")throw new InvalidOperationException("Completed race selection did not display its drivers immediately.");
        if(dialog.RosterEditActions.IsEnabled || dialog.RemoveHeatButton.IsEnabled || dialog.AddHeatButton.IsEnabled || dialog.HeatUpButton.IsEnabled)throw new InvalidOperationException("Completed race editing controls remain enabled.");
        if(((HeatPlan)dialog.HeatGrid.SelectedItem).SessionDisplay!=label || label!="Q1 • Heat 1/2")throw new InvalidOperationException("Session label changed on selection.");
        if(dialog.SavedSessionGrid.IsVisible)throw new InvalidOperationException("Duplicate lower race list is still visible.");
        if(dialog.HeatGrid.Items.Count!=6 || dialog._browsingSavedLineups || dialog.HeatPlans.Count!=2)throw new InvalidOperationException($"Selecting a race narrowed the overview or failed to open its roster: visible={dialog.HeatGrid.Items.Count}, browsing={dialog._browsingSavedLineups}, editing={dialog.HeatPlans.Count}.");
        var other=dialog.HeatGrid.Items.Cast<HeatPlan>().First(h=>h.ClassId==classes[1].Id && h.Round==2);dialog.HeatGrid.SelectedItem=other;
        if(dialog.HeatGrid.Items.Count!=6 || dialog.HeatPlans.Count!=1 || dialog.HeatPlans[0].Round!=2)throw new InvalidOperationException("Changing class/round hid the lineup.");
        if(dialog.EntrantGrid.Items.Count!=1 || !dialog.RosterEditActions.IsEnabled)throw new InvalidOperationException("Unrun race selection did not display editable drivers immediately.");
        dialog.AddHeat_Click(dialog,new RoutedEventArgs());
        if(dialog.HeatGrid.Items.Count!=7 || dialog.HeatPlans.Count!=2 || dialog.HeatPlans.Any(h=>h.ClassId!=classes[1].Id || h.Round!=2))throw new InvalidOperationException("Add heat used the wrong class/round.");
        dialog.RemoveHeat_Click(dialog,new RoutedEventArgs());
        if(dialog.HeatGrid.Items.Count!=6 || ev.Sessions.Count!=6 || dialog.HeatPlans.Any(h=>h.ClassId!=classes[1].Id || h.Round!=2))throw new InvalidOperationException("Removing preview heat changed saved sessions or overview.");
        if(System.Text.Json.JsonSerializer.Serialize(ev.Sessions[0])!=originalCompleted)throw new InvalidOperationException("Browsing changed completed race data.");
        var dragSource=ev.Sessions.First(s=>s.ClassId==classes[1].Id && s.Round==1);
        var dragTarget=ev.Sessions.First(s=>s.ClassId==classes[0].Id && s.Round==1 && s.ProgressStatus==EventSessionStatus.Planned);
        var savedQueue=ev.Sessions.ToDictionary(s=>s.Id,s=>s.QueueOrder);
        dialog.StageRaceMove(dragSource.Id,dragTarget.Id);
        if(!dialog.SaveButton.IsEnabled || dialog.PendingRaceOrder.Count==0 || ev.Sessions.Any(s=>s.QueueOrder!=savedQueue[s.Id]))throw new InvalidOperationException("Drag did not stage race order without modifying saved races.");
        if(System.Text.Json.JsonSerializer.Serialize(ev.Sessions[0])!=originalCompleted)throw new InvalidOperationException("Drag changed completed race.");
        if(System.Windows.Data.CollectionViewSource.GetDefaultView(dialog.HeatGrid.ItemsSource).Groups?.Count!=2)throw new InvalidOperationException("Qualifying rounds are not grouped.");
        dialog.UpdateLayout();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth),(int)Math.Ceiling(dialog.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(dialog);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using(var file=System.IO.File.Create(System.IO.Path.Combine(output,"Lineup-Overview.png")))encoder.Save(file);
        dialog._lineupSnapshot=null;
        dialog.SavedRoundFilter.SelectedItem=((IEnumerable<SavedRoundChoice>)dialog.SavedRoundFilter.ItemsSource).Single(r=>r.Round==2);
        if(dialog.HeatGrid.Items.Count!=2 || dialog.HeatGrid.Items.Cast<HeatPlan>().Any(h=>h.Round!=2))throw new InvalidOperationException("Specific round filter includes other rounds.");
        dialog._pendingRaceOrder.Clear();
        dialog._lineupSnapshot=null;
        dialog.SavedRoundFilter.SelectedIndex=0;
        dialog.OpenSavedLineup(ev.Sessions.First(s=>s.ClassId==classes[1].Id && s.Round==1).Id);
        var driverSource=dialog.HeatPlans.First();var driverTarget=dialog.HeatPlans.Last();var driver=driverSource.Entrants.Single();
        var originalEvent=System.Text.Json.JsonSerializer.Serialize(ev);
        if(dialog.DriverMoveError(driverSource,driverSource,driver) is null)throw new InvalidOperationException("Driver self-drop accepted.");
        var lockedTarget=new HeatPlan {ClassId=driverSource.ClassId,Round=driverSource.Round,IsLocked=true};
        if(dialog.DriverMoveError(driverSource,lockedTarget,driver) is null)throw new InvalidOperationException("Driver drop into completed race accepted.");
        var wrongClass=dialog.HeatGrid.Items.Cast<HeatPlan>().First(h=>h.ClassId!=driverSource.ClassId);
        if(dialog.DriverMoveError(driverSource,wrongClass,driver) is null)throw new InvalidOperationException("Driver cross-class drop accepted.");
        driverTarget.Entrants.Add(CloneEntrant(driver));
        if(dialog.DriverMoveError(driverSource,driverTarget,driver) is null)throw new InvalidOperationException("Duplicate driver drop accepted.");
        driverTarget.Entrants.RemoveAt(driverTarget.Entrants.Count-1);
        driver.TransponderId="driver-drag-test";driverTarget.Entrants[0].TransponderId=driver.TransponderId;
        if(dialog.DriverMoveError(driverSource,driverTarget,driver) is null)throw new InvalidOperationException("Duplicate transponder drop accepted.");
        driverTarget.Entrants[0].TransponderId="";
        while(driverTarget.Entrants.Count<30)driverTarget.Entrants.Add(new SessionEntrant {DriverId=Guid.NewGuid().ToString(),DriverName="Capacity test"});
        if(dialog.DriverMoveError(driverSource,driverTarget,driver) is null)throw new InvalidOperationException("Driver drop into full race accepted.");
        while(driverTarget.Entrants.Count>1)driverTarget.Entrants.RemoveAt(driverTarget.Entrants.Count-1);
        if(driverSource.Entrants.Count!=1)throw new InvalidOperationException("Rejected driver drops changed source race.");
        if(!dialog.MoveLineupDriver(driverSource,driverTarget,driver) || driverSource.Entrants.Count!=0 || driverTarget.Entrants.Count!=2 || driverTarget.Entrants.Last().CarNumber!=2)throw new InvalidOperationException("Driver drop did not move and renumber driver.");
        dialog.HeatGrid.SelectedItem=driverTarget;dialog.EntrantGrid.SelectedItem=driver;dialog.TargetHeatCombo.SelectedItem=driverSource;
        dialog.MoveDriver_Click(dialog,new RoutedEventArgs());
        if(driverSource.Entrants.Count!=1 || driverTarget.Entrants.Count!=1 || driver.CarNumber!=1)throw new InvalidOperationException("Move driver button no longer works alongside drag.");
        if(System.Text.Json.JsonSerializer.Serialize(ev)!=originalEvent)throw new InvalidOperationException("Driver drop changed saved event before Save.");
        var firstEditedId=driverSource.SourceSessionId;
        driverSource.Entrants[0].DriverName="First draft edit";
        var secondEditedId=ev.Sessions.First(s=>s.Round==2 && s.ClassId==classes[0].Id).Id;
        dialog.OpenSavedLineup(secondEditedId);
        if(dialog.HeatPlans.Single().SourceSessionId!=secondEditedId)throw new InvalidOperationException("A pending edit blocked switching to another round/class.");
        dialog.HeatPlans[0].Entrants[0].DriverName="Second draft edit";
        dialog.SavedRoundFilter.SelectedItem=((IEnumerable<SavedRoundChoice>)dialog.SavedRoundFilter.ItemsSource).Single(r=>r.Round==1);
        if(!dialog.SaveButton.IsEnabled || dialog._stagedLineups.Count!=2)throw new InvalidOperationException("A round filter discarded pending changes or disabled Save.");
        dialog.OpenSavedLineup(firstEditedId);
        if(dialog.HeatPlans.Single(h=>h.SourceSessionId==firstEditedId).Entrants[0].DriverName!="First draft edit")throw new InvalidOperationException("Reopening a scope did not restore its draft.");
        dialog.PrepareSaveBatch();
        if(dialog.OrderOnlySave || !dialog.BuiltSessions.Any(s=>s.Id==firstEditedId && s.Entrants[0].DriverName=="First draft edit") ||
            !dialog.BuiltSessions.Any(s=>s.Id==secondEditedId && s.Entrants[0].DriverName=="Second draft edit"))throw new InvalidOperationException("One Save did not include both edited scopes.");
        if(System.Text.Json.JsonSerializer.Serialize(ev)!=originalEvent)throw new InvalidOperationException("Staging scopes changed the saved event before Save.");
        dialog._stagedLineups.Clear();dialog._lineupSnapshot=null;dialog.Close();
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"Lineup-Draft-Checks.txt"),"PASS: switch class/round/filter without saving, restore prior edits, collect both scopes in one save and preserve the original event before save.");
        var combinedEvent=CombinedRaces.Clone(ev);
        foreach(var saved in combinedEvent.Sessions)saved.ProgressStatus=EventSessionStatus.Planned;
        var combinedDialog=new RoundMainBuilderDialog(combinedEvent,classes,[]);combinedDialog.Show();combinedDialog.UpdateLayout();
        var members=combinedEvent.Sessions.Where(s=>s.Round==2).ToList();
        var beforeCombine=System.Text.Json.JsonSerializer.Serialize(combinedEvent);
        var selectionRows=combinedDialog.HeatGrid.Items.Cast<HeatPlan>().Where(h=>h.Round==2).ToList();
        combinedDialog.BeginCombineSelection();
        foreach(var row in selectionRows)combinedDialog.ToggleCombineRace(row);
        if(combinedDialog.HeatGrid.SelectedItems.Count!=2 || combinedDialog._combineSelection.Count!=2 || combinedDialog.HeatGrid.Cursor!=Cursors.Hand)throw new InvalidOperationException("Shift selection did not retain multiple races and hand cursor.");
        combinedDialog.CancelCombineSelection();
        if(combinedDialog.HeatGrid.SelectedItems.Count!=0 || combinedDialog._combineSelection.Count!=0 || combinedDialog.HeatGrid.Cursor is not null)throw new InvalidOperationException("Shift selection did not restore its cursor/selection.");
        combinedDialog.StageCombination(members);
        var combinedRows=combinedDialog.HeatGrid.Items.Cast<HeatPlan>().ToList();
        if(combinedRows.Count!=7 || combinedRows.Count(h=>h.IsCombinedHeader)!=1 || combinedRows.Count(h=>h.IsCombinedChild)!=2)throw new InvalidOperationException("Combined lineup does not contain one parent and two indented heats.");
        if(System.Text.Json.JsonSerializer.Serialize(combinedEvent)!=beforeCombine)throw new InvalidOperationException("Combined preview changed source event before Save.");
        var child=combinedRows.First(h=>h.IsCombinedChild);
        combinedDialog.HeatGrid.SelectedItem=child;
        if(combinedDialog.EntrantGrid.Items.Count!=1)throw new InvalidOperationException("Combined child does not show its own editable roster.");
        if(combinedDialog.HeatGrid.Items.Cast<HeatPlan>().Count(h=>h.IsCombinedChild)!=2)throw new InvalidOperationException("Selecting combined child hid its partner.");
        combinedDialog.UpdateLayout();
        MainWindow.CheckCompactRunRow(combinedDialog.HeatGrid,
            item => item is HeatPlan {IsCombinedHeader:true},
            item => item is HeatPlan {IsCombinedHeader:false,IsCombinedChild:false});
        var combinedBitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(combinedDialog.ActualWidth),(int)Math.Ceiling(combinedDialog.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);combinedBitmap.Render(combinedDialog);
        var combinedEncoder=new System.Windows.Media.Imaging.PngBitmapEncoder();combinedEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(combinedBitmap));
        using(var file=System.IO.File.Create(System.IO.Path.Combine(output,"Combined-Lineup.png")))combinedEncoder.Save(file);
        var scheduleFixture=CombinedRaces.Clone(combinedEvent);
        var scheduledMembers=scheduleFixture.Sessions.Where(s=>s.Round==2).ToList();
        for(var i=0;i<scheduledMembers.Count;i++)
        {
            scheduledMembers[i].CombinedRaceId="preview-combined";
            scheduledMembers[i].CombinedRaceOrder=i;
            scheduledMembers[i].ProgressStatus=EventSessionStatus.Loaded;
        }
        MainWindow.ExportEventRunsPreview(output,scheduleFixture);
        combinedDialog.SeparateCombined_Click(combinedDialog,new RoutedEventArgs());
        if(combinedDialog.HeatGrid.Items.Count!=6 || combinedDialog.HeatGrid.Items.Cast<HeatPlan>().Any(h=>h.IsCombinedHeader || h.IsCombinedChild))throw new InvalidOperationException("Separate did not restore independent race rows.");
        combinedDialog._pendingCombinedRaces.Clear();combinedDialog._lineupSnapshot=null;combinedDialog.Close();
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"Combined-Lineup-Checks.txt"),"PASS: combined header, indented children, partner visible when selecting a child, individual roster, staged metadata, separation, no event mutation before Save.");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"Driver-Drag-Checks.txt"),"PASS: shared button/drop movement, renumbering, cancelled/self/wrong-class/completed/full/duplicate driver/duplicate transponder drops leave lineups intact; saved event unchanged before Save.");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"Race-Order-Checks.txt"),"PASS: drag stages order, saved event unchanged before Save, completed race protected, round headers present, specific round filter works.");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"Unified-Lineup-Checks.txt"),"PASS: one visible race list; stable Q1 Heat labels; completed and unrun rosters display on first selection; completed edit/remove/add/reorder controls disabled; saved completed data unchanged.");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"Lineup-Overview-Checks.txt"),"PASS: all classes/rounds remain visible while selecting, editing, adding and removing a heat; source sessions unchanged before saving.");
    }
}
