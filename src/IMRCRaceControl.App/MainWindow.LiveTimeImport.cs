using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using IMRCRaceControl.Services;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private void OpenLiveTimeImport(Window owner)
    {
        var dialog=new Window
        {
            Owner=owner,Title="Import LiveTime data",Width=760,SizeToContent=SizeToContent.Height,MaxHeight=SystemParameters.WorkArea.Height*0.90,
            WindowStartupLocation=WindowStartupLocation.CenterOwner
        };
        var panel=new StackPanel {Margin=new Thickness(20)};
        dialog.Content=new ScrollViewer { Content=panel, VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock {Text="IMPORT LIVETIME DATA",FontSize=22,FontWeight=FontWeights.Bold});
        panel.Children.Add(new TextBlock
        {
            Text="Choose roster categories and events to import. Preview the changes before saving. Existing events and personal transponders are kept.",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)
        });

        string? sourcePath=null, referencePath=null;
        LiveTimeImportSource? source=null;
        List<RaceDayEventDefinition> eventPlan=[];
        LiveTimeRosterPlan? plan=null;
        var choices=new WrapPanel();
        var select=new Button {Content="CHOOSE LIVETIME BACKUP OR IMPORT COPY…"};
        var reference=new Button {Content="OPTIONAL REFERENCE BACKUP…"};
        choices.Children.Add(select);choices.Children.Add(reference);panel.Children.Add(choices);
        var sourceLabel=new TextBlock {Text="No backup selected.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,9,0,4)};
        var status=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,8)};
        var issues=new ListBox {MaxHeight=145,Visibility=Visibility.Collapsed};
        panel.Children.Add(sourceLabel);
        var categories = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        var toggles = new (CheckBox Control, LiveTimeRosterSelection Option)[]
        {
            (new CheckBox { Content = "Drivers" }, LiveTimeRosterSelection.Drivers),
            (new CheckBox { Content = "Classes" }, LiveTimeRosterSelection.Classes),
            (new CheckBox { Content = "Driver–class assignments" }, LiveTimeRosterSelection.ClassAssignments),
            (new CheckBox { Content = "Personal transponders" }, LiveTimeRosterSelection.Transponders)
        };
        foreach (var (control, _) in toggles)
        {
            control.Margin = new Thickness(0, 0, 14, 4);
            control.IsChecked = true;
            categories.Children.Add(control);
        }
        categories.IsEnabled = false;
        panel.Children.Add(categories);
        var historyOptions = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 4) };
        var eventActions = new WrapPanel();
        eventActions.Children.Add(new TextBlock { Text = "COMPLETED RACES BY EVENT", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        var selectAll = new Button { Content = "SELECT ALL" };
        var clearEvents = new Button { Content = "CLEAR" };
        eventActions.Children.Add(selectAll);
        eventActions.Children.Add(clearEvents);
        historyOptions.Children.Add(eventActions);
        historyOptions.Children.Add(new TextBlock { Text = "Click events to select or deselect them. Finishing results are included with each selected event.", TextWrapping = TextWrapping.Wrap });
        var eventList = new ListBox { SelectionMode = SelectionMode.Multiple, DisplayMemberPath = nameof(LiveTimeEventHistory.Label), MaxHeight = 165 };
        historyOptions.Children.Add(eventList);
        var includeLaps = new CheckBox { Content = "Include individual scored laps", Margin = new Thickness(0, 5, 0, 0), IsChecked = true };
        historyOptions.Children.Add(includeLaps);
        panel.Children.Add(historyOptions);
        panel.Children.Add(status);
        panel.Children.Add(issues);
        var actions=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var commit=new Button {Content="IMPORT SELECTED",IsEnabled=false};
        var close=new Button {Content="CLOSE",IsCancel=true};
        actions.Children.Add(commit);actions.Children.Add(close);panel.Children.Add(actions);
        var busy=false;
        void SetBusy(bool value)
        {
            busy=value;select.IsEnabled=reference.IsEnabled=close.IsEnabled=!value;
            categories.IsEnabled=historyOptions.IsEnabled=!value && source is not null;
            commit.IsEnabled=!value && (plan?.HasChanges==true || eventPlan.Count>0);
        }
        dialog.Closing+=(_,e)=>{if(busy)e.Cancel=true;};

        string? Pick(string title)
        {
            var picker=new OpenFileDialog {Title=title,Filter="LiveTime database or backup (*.db;*.bak;*.sqlite)|*.db;*.bak;*.sqlite|All files (*.*)|*.*"};
            return picker.ShowDialog(dialog)==true ? picker.FileName : null;
        }

        void UpdatePreview()
        {
            if (source is null) return;
            var roster = source.Roster;
            var chosen = eventList.SelectedItems.Cast<LiveTimeEventHistory>().Select(e => e.SourceId).ToList();
            var selection = toggles.Where(t => t.Control.IsChecked == true)
                .Aggregate(LiveTimeRosterSelection.None, (selected, toggle) => selected | toggle.Option);
            plan = LiveTimeRosterMerge.Preview(roster, Drivers.ToList(), Classes.ToList(), _driverAssignments.ToList(),
                _settings.RentalTransponders.Inventory.Select(t => t.Number), selection, chosen.Count > 0);
            eventPlan = LiveTimeHistoryMerge.Preview(source, chosen, Events.ToList(), plan, includeLaps.IsChecked == true);
            var newTags = plan.Assignments.Count(a => a.TransponderId.Length > 0);
            status.Text = $"Source: {roster.Drivers.Count} active drivers • {roster.Classes.Count} active classes • {roster.Entries.Count} driver–class assignments\n"+
                $"Add {plan.Drivers.Count} drivers, {plan.Classes.Count} classes and {plan.Assignments.Count} assignments ({newTags} with transponders). Fill transponders on {plan.TagFills.Count} existing assignments.\n"+
                $"History in this backup: {roster.Events} events, {roster.Races} races, {roster.Laps} race laps, {roster.PracticeLaps} practice laps and {roster.Rentals} rentals.";
            if (source.History.Count > 0)
                status.Text += $"\nSelected: {eventPlan.Count} new events, {eventPlan.Sum(e => e.Sessions.Count)} completed races and {eventPlan.Sum(e => e.Sessions.Sum(r => r.Laps.Count)):N0} scored laps.";
            if (plan.SkippedMissingAssignments > 0)
                status.Text += $"\nSkipped {plan.SkippedMissingAssignments} assignments without a matching driver, class or existing assignment. Select the missing categories to include them.";
            if (!plan.HasChanges && eventPlan.Count == 0)
                status.Text += selection == LiveTimeRosterSelection.None && chosen.Count == 0 ? "\nChoose categories or events to import." : "\nNo new records to import with this selection.";
            if (plan.Issues.Count > 0)
                status.Text += $"\n{plan.Issues.Count} conflicts or skipped items:";
            issues.ItemsSource = plan.Issues.Take(200).ToList();
            issues.Visibility = plan.Issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            commit.IsEnabled = !busy && (plan.HasChanges || eventPlan.Count > 0);
        }

        foreach (var (control, _) in toggles)
        {
            control.Checked += (_, _) => UpdatePreview();
            control.Unchecked += (_, _) => UpdatePreview();
        }

        void RefreshAvailableEvents()
        {
            var imported = Events.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var available = source?.History.Where(e => !imported.Contains(LiveTimeHistoryMerge.EventId(e.SourceId))).ToList() ?? [];
            eventList.ItemsSource = available;
            historyOptions.Visibility = available.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        selectAll.Click += (_, _) => eventList.SelectAll();
        clearEvents.Click += (_, _) => eventList.UnselectAll();
        eventList.SelectionChanged += (_, _) => UpdatePreview();
        includeLaps.Checked += (_, _) => UpdatePreview();
        includeLaps.Unchecked += (_, _) => UpdatePreview();

        async Task PreviewAsync()
        {
            if(sourcePath is null)return;
            source=null;plan=null;eventPlan=[];SetBusy(true);RefreshAvailableEvents();issues.Visibility=Visibility.Collapsed;
            status.Text="Reading backup…";
            sourceLabel.Text=Path.GetFileName(sourcePath)+(referencePath is null ? "" : $" • Reference: {Path.GetFileName(referencePath)}");
            try
            {
                source=await Task.Run(()=>LiveTimeBackupReader.ReadImport(sourcePath,referencePath));
                RefreshAvailableEvents();
                UpdatePreview();
            }
            catch(Exception ex) {status.Text=ex.Message;}
            finally {SetBusy(false);}
        }

        select.Click+=async (_,_)=>{if(Pick("Select a LiveTime database or backup") is { } path){sourcePath=path;await PreviewAsync();}};
        reference.Click+=async (_,_)=>{if(Pick("Select an older backup from the same LiveTime installation") is { } path){referencePath=path;await PreviewAsync();}};
        commit.Click+=async (_,_) =>
        {
            if(plan is not { } additions || (!additions.HasChanges && eventPlan.Count == 0) || !ApplicationUpdateAllowed)return;
            var importedEvents = eventPlan;
            SetBusy(true);
            var applied=false;
            var updated = new List<(DriverClassAssignment Assignment, string PreviousTags)>();
            try
            {
                await CaptureAndSaveSettingsAsync(true);
                await DataProtectionService.BackupAsync(ApplicationDataPaths.DatabasePath,ApplicationDataPaths.BackupDirectory,true);
                applied=true;
                foreach(var driver in additions.Drivers)Drivers.Add(driver);
                foreach(var cls in additions.Classes)Classes.Add(cls);
                foreach(var assignment in additions.Assignments)_driverAssignments.Add(assignment);
                foreach (var fill in additions.TagFills)
                {
                    var assignment = _driverAssignments.FirstOrDefault(a => a.Id == fill.AssignmentId);
                    if (assignment is null || !string.IsNullOrWhiteSpace(assignment.TransponderId))
                        throw new InvalidOperationException("An assignment changed after the preview. Reopen the import to preview it again.");
                    updated.Add((assignment, assignment.TransponderId));
                    assignment.TransponderId = fill.Transponders;
                }
                foreach (var ev in importedEvents) Events.Add(ev);
                await CaptureAndSaveSettingsAsync(true);
            }
            catch(Exception ex)
            {
                if(applied)
                {
                    foreach(var driver in additions.Drivers)Drivers.Remove(driver);
                    foreach(var cls in additions.Classes)Classes.Remove(cls);
                    foreach(var assignment in additions.Assignments)_driverAssignments.Remove(assignment);
                    foreach (var (assignment, previous) in updated) assignment.TransponderId = previous;
                    foreach (var ev in importedEvents) Events.Remove(ev);
                    _settings.Events=Events.ToList();
                    _settings.Drivers=Drivers.ToList();_settings.Classes=Classes.ToList();
                    _settings.DriverClassAssignments=_driverAssignments.ToList();
                }
                status.Text="Import did not complete: "+ex.Message;
                return;
            }
            finally {SetBusy(false);}
            RefreshAssignmentRows();
            RefreshEventSetupCollections();
            RefreshAvailableEvents();
            UpdatePreview();
            status.Text=$"Imported {additions.Drivers.Count} drivers, {additions.Classes.Count} classes and {additions.Assignments.Count} assignments. Filled transponders on {additions.TagFills.Count} existing assignments. Imported {importedEvents.Count} events and {importedEvents.Sum(e => e.Sessions.Count)} completed races.";
            FooterStatusText.Text="Selected LiveTime data imported.";
        };
        dialog.ShowDialog();
    }
}
