using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using IMRCRaceControl.Services;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private void OpenLiveTimeImport(Window owner)
    {
        var dialog=new Window
        {
            Owner=owner,Title="Import LiveTime roster",Width=700,SizeToContent=SizeToContent.Height,
            WindowStartupLocation=WindowStartupLocation.CenterOwner
        };
        var panel=new StackPanel {Margin=new Thickness(20)};
        dialog.Content=panel;
        panel.Children.Add(new TextBlock {Text="IMPORT LIVETIME ROSTER",FontSize=22,FontWeight=FontWeights.Bold});
        panel.Children.Add(new TextBlock
        {
            Text="Preview active drivers, classes and transponder assignments from a supported LiveTime backup. Existing Race Control records are kept. Race history and rentals are not imported by this tool.",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)
        });

        string? sourcePath=null, referencePath=null;
        LiveTimeRosterPlan? plan=null;
        var choices=new WrapPanel();
        var select=new Button {Content="CHOOSE LIVETIME BACKUP…"};
        var reference=new Button {Content="OPTIONAL REFERENCE BACKUP…"};
        choices.Children.Add(select);choices.Children.Add(reference);panel.Children.Add(choices);
        var sourceLabel=new TextBlock {Text="No backup selected.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,9,0,4)};
        var status=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,8)};
        var issues=new ListBox {MaxHeight=145,Visibility=Visibility.Collapsed};
        panel.Children.Add(sourceLabel);panel.Children.Add(status);panel.Children.Add(issues);
        var actions=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var commit=new Button {Content="IMPORT ROSTER",IsEnabled=false};
        var close=new Button {Content="CLOSE",IsCancel=true};
        actions.Children.Add(commit);actions.Children.Add(close);panel.Children.Add(actions);
        var busy=false;
        void SetBusy(bool value)
        {
            busy=value;select.IsEnabled=reference.IsEnabled=close.IsEnabled=!value;
            commit.IsEnabled=!value && plan?.HasChanges==true;
        }
        dialog.Closing+=(_,e)=>{if(busy)e.Cancel=true;};

        string? Pick(string title)
        {
            var picker=new OpenFileDialog {Title=title,Filter="LiveTime database or backup (*.db;*.bak;*.sqlite)|*.db;*.bak;*.sqlite|All files (*.*)|*.*"};
            return picker.ShowDialog(dialog)==true ? picker.FileName : null;
        }

        async Task PreviewAsync()
        {
            if(sourcePath is null)return;
            plan=null;SetBusy(true);issues.Visibility=Visibility.Collapsed;
            status.Text="Reading backup…";
            sourceLabel.Text=Path.GetFileName(sourcePath)+(referencePath is null ? "" : $" • Reference: {Path.GetFileName(referencePath)}");
            try
            {
                var roster=await Task.Run(()=>LiveTimeBackupReader.Read(sourcePath,referencePath));
                plan=LiveTimeRosterMerge.Preview(roster,Drivers.ToList(),Classes.ToList(),_driverAssignments.ToList(),
                    _settings.RentalTransponders.Inventory.Select(t=>t.Number));
                status.Text=$"LiveTime database version {roster.SchemaVersion} • {roster.Drivers.Count} active drivers • {roster.Classes.Count} active classes\n"+
                    $"Add {plan.Drivers.Count} drivers, {plan.Classes.Count} classes and {plan.Assignments.Count} assignments.\n"+
                    $"Match {plan.MatchedDrivers} drivers, {plan.MatchedClasses} classes and {plan.MatchedAssignments} assignments already here.\n"+
                    $"Source history: {roster.Events} events, {roster.Races} races, {roster.Laps} race laps and {roster.PracticeLaps} practice laps. Rentals: {roster.Rentals}.\n"+
                    (roster.Drivers.Count==0 && roster.Classes.Count==0 ? "This backup has no roster to import." :
                        plan.HasChanges ? "Review the preview, then click Import Roster to save." : "No new roster records to import.");
                if(plan.Issues.Count>0)
                {
                    status.Text+=$"\n{plan.Issues.Count} conflicts or skipped items:";
                    issues.ItemsSource=plan.Issues.Take(200).ToList();
                    issues.Visibility=Visibility.Visible;
                }
            }
            catch(Exception ex) {status.Text=ex.Message;}
            finally {SetBusy(false);}
        }

        select.Click+=async (_,_)=>{if(Pick("Select a LiveTime database or backup") is { } path){sourcePath=path;await PreviewAsync();}};
        reference.Click+=async (_,_)=>{if(Pick("Select an older backup from the same LiveTime installation") is { } path){referencePath=path;await PreviewAsync();}};
        commit.Click+=async (_,_) =>
        {
            if(plan is not {HasChanges:true} additions || !ApplicationUpdateAllowed)return;
            SetBusy(true);
            var applied=false;
            try
            {
                await CaptureAndSaveSettingsAsync(true);
                await DataProtectionService.BackupAsync(ApplicationDataPaths.DatabasePath,ApplicationDataPaths.BackupDirectory,true);
                applied=true;
                foreach(var driver in additions.Drivers)Drivers.Add(driver);
                foreach(var cls in additions.Classes)Classes.Add(cls);
                foreach(var assignment in additions.Assignments)_driverAssignments.Add(assignment);
                await CaptureAndSaveSettingsAsync(true);
            }
            catch(Exception ex)
            {
                if(applied)
                {
                    foreach(var driver in additions.Drivers)Drivers.Remove(driver);
                    foreach(var cls in additions.Classes)Classes.Remove(cls);
                    foreach(var assignment in additions.Assignments)_driverAssignments.Remove(assignment);
                    _settings.Drivers=Drivers.ToList();_settings.Classes=Classes.ToList();
                    _settings.DriverClassAssignments=_driverAssignments.ToList();
                }
                status.Text="Roster was not imported: "+ex.Message;
                return;
            }
            finally {SetBusy(false);}
            RefreshAssignmentRows();
            RefreshEventSetupCollections();
            plan=null;commit.IsEnabled=false;
            status.Text=$"Imported {additions.Drivers.Count} drivers, {additions.Classes.Count} classes and {additions.Assignments.Count} assignments.";
            FooterStatusText.Text="LiveTime roster imported. Review class race settings before racing.";
        };
        dialog.ShowDialog();
    }
}
