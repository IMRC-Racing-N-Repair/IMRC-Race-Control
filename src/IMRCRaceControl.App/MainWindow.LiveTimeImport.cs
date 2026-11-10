using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
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
            Owner=owner,Title="Import LiveTime data",Width=Math.Min(700, SystemParameters.WorkArea.Width * 0.95),SizeToContent=SizeToContent.Height,MaxHeight=SystemParameters.WorkArea.Height*0.90,
            WindowStartupLocation=WindowStartupLocation.CenterOwner
        };
        var panel=new StackPanel {Margin=new Thickness(20)};
        dialog.Content=new ScrollViewer { Content=panel, VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock {Text="IMPORT LIVETIME DATA",FontSize=22,FontWeight=FontWeights.Bold});
        panel.Children.Add(new TextBlock
        {
            Text="Open a LiveTime backup, then select the categories you want to import. The buttons show how many records are in the backup.",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)
        });

        string? sourcePath=null, referencePath=null;
        LiveTimeImportSource? source=null;
        List<RaceDayEventDefinition> eventPlan = [];
        List<(RaceDayEventDefinition Event, TrackProfile Track)> trackPlan = [];
        LiveTimeRosterPlan? plan=null;
        var choices=new WrapPanel();
        var select=new Button {Content="CHOOSE LIVETIME DATABASE…"};
        var reference=new Button {Content="REFERENCE FOR OLDER BACKUPS…"};
        choices.Children.Add(select);choices.Children.Add(reference);panel.Children.Add(choices);
        var sourceLabel=new TextBlock {Text="No backup selected.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,9,0,4)};
        var status=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,8)};
        panel.Children.Add(sourceLabel);
        var unlockPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 4) };
        var passwordBox = new PasswordBox { MaxLength = 512, MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Stretch };
        unlockPanel.Children.Add(new Label { Content = "Backup password or _unlock key", Target = passwordBox });
        var unlockRow = new DockPanel { LastChildFill = true };
        var unlock = new Button { Content = "UNLOCK & PREVIEW", Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(unlock, Dock.Right);
        unlockRow.Children.Add(unlock);
        unlockRow.Children.Add(passwordBox);
        unlockPanel.Children.Add(unlockRow);
        var unlockFile = new Button { Content = "CHOOSE UNLOCK KEY FILE…", HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 5, 0, 0) };
        unlockPanel.Children.Add(unlockFile);
        unlockPanel.Children.Add(new TextBlock
        {
            Text = "Race Control does not save your password or create a decrypted copy on disk.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
        });
        panel.Children.Add(unlockPanel);
        var categories = new WrapPanel { Margin = new Thickness(0, 8, 0, 4), IsEnabled = false };
        var toggles = new[]
        {
            AddCategory("Drivers", LiveTimeRosterSelection.Drivers),
            AddCategory("Classes", LiveTimeRosterSelection.Classes),
            AddCategory("Class assignments", LiveTimeRosterSelection.ClassAssignments),
            AddCategory("Personal transponders", LiveTimeRosterSelection.Transponders),
            AddCategory("Class settings", LiveTimeRosterSelection.ClassSettings),
            AddCategory("Events & races", LiveTimeRosterSelection.None)
        };
        var eventToggle = toggles[^1].Control;
        var eventList = new ListBox { SelectionMode = SelectionMode.Multiple, DisplayMemberPath = nameof(LiveTimeEventHistory.Label),
            MaxHeight = 175 };
        var eventSelector = new Expander { Content = eventList, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 8) };
        var optionsHint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) };
        optionsHint.Text = "Class settings updates matching classes. Events & races includes completed races and scored laps.";
        panel.Children.Add(categories);
        panel.Children.Add(optionsHint);
        panel.Children.Add(eventSelector);
        panel.Children.Add(status);

        (ToggleButton Control, TextBlock Count, string Label, LiveTimeRosterSelection Option) AddCategory(
            string label, LiveTimeRosterSelection option)
        {
            var count = new TextBlock { Text = "—", FontSize = 21, FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center };
            var content = new StackPanel();
            content.Children.Add(count);
            content.Children.Add(new TextBlock { Text = label, TextAlignment = TextAlignment.Center });
            var button = new ToggleButton { Content = content, IsEnabled = false };
            button.SetResourceReference(FrameworkElement.StyleProperty, "ImportCategoryButtonStyle");
            AutomationProperties.SetName(button, label);
            categories.Children.Add(button);
            return (button, count, label, option);
        }

        void SetCategoryCounts(LiveTimeImportSource? import)
        {
            var roster = import?.Roster;
            int[] counts = roster is null ? [] : [
                roster.Drivers.Count, roster.Classes.Count, roster.Entries.Count,
                LiveTimeRosterMerge.CountPersonalTransponders(roster),
                roster.Classes.Count(c => c.MinimumLapSeconds.HasValue || c.Timing is not null),
                import!.History.Count];
            for (var i = 0; i < toggles.Length; i++)
            {
                var (control, count, label, _) = toggles[i];
                count.Text = roster is null ? "—" : counts[i].ToString("N0");
                control.IsEnabled = roster is not null && counts[i] > 0;
                control.IsChecked = roster is not null && counts[i] > 0 && i < 4;
                AutomationProperties.SetName(control, roster is null ? label : $"{label}: {count.Text} in backup");
            }
            eventList.ItemsSource = import?.History;
            eventList.SelectAll();
            eventSelector.IsExpanded = false;
            eventSelector.Visibility = Visibility.Collapsed;
        }
        var actions=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var commit=new Button {Content="IMPORT SELECTED",IsEnabled=false};
        var close=new Button {Content="CLOSE",IsCancel=true};
        actions.Children.Add(commit);actions.Children.Add(close);panel.Children.Add(actions);
        var busy=false;
        void SetBusy(bool value)
        {
            busy=value;select.IsEnabled=reference.IsEnabled=close.IsEnabled=unlock.IsEnabled=unlockFile.IsEnabled=passwordBox.IsEnabled=!value;
            categories.IsEnabled=!value && source is not null;
            eventSelector.IsEnabled = !value;
            commit.IsEnabled=!value && (plan?.HasChanges==true || eventPlan.Count > 0 || trackPlan.Count > 0);
        }
        dialog.Closing+=(_,e)=>{if(busy)e.Cancel=true;else passwordBox.Clear();};

        string? Pick(string title)
        {
            var picker=new OpenFileDialog {Title=title,Filter="LiveTime database or backup (*.db;*.bak;*.sqlite)|*.db;*.bak;*.sqlite|All files (*.*)|*.*"};
            return picker.ShowDialog(dialog)==true ? picker.FileName : null;
        }

        void UpdatePreview()
        {
            if (busy || source is null) return;
            var selection = toggles.Where(t => t.Control.IsChecked == true)
                .Aggregate(LiveTimeRosterSelection.None, (selected, toggle) => selected | toggle.Option);
            var includeHistory = eventToggle.IsChecked == true;
            eventSelector.Visibility = includeHistory ? Visibility.Visible : Visibility.Collapsed;
            var chosen = includeHistory ? eventList.SelectedItems.OfType<LiveTimeEventHistory>().ToList() : [];
            plan = LiveTimeRosterMerge.Preview(source.Roster, Drivers.ToList(), Classes.ToList(), _driverAssignments.ToList(),
                _settings.RentalTransponders.Inventory.Select(t => t.Number), selection, resolveForHistory: chosen.Count > 0);
            var chosenIds = chosen.Select(e => e.SourceId).ToList();
            eventPlan = chosen.Count == 0 ? [] : LiveTimeHistoryMerge.Preview(source, chosenIds,
                Events, plan, includeLaps: true, savedTrack: _settings.Track);
            trackPlan = chosen.Count == 0 ? [] : LiveTimeHistoryMerge.MissingTrackLinks(source, chosenIds, Events, _settings.Track);
            if (includeHistory)
                eventSelector.Header = $"Choose events ({chosen.Count:N0} selected • {chosen.Sum(e => e.RaceCount):N0} completed races)";
            status.Text = selection == LiveTimeRosterSelection.None && !includeHistory ? "Select the categories to import." :
                plan.HasChanges || eventPlan.Count > 0 || trackPlan.Count > 0 ? "Ready to import." : "No new records to import with this selection.";
            if (plan.ClassUpdates.Count > 0)
                status.Text += $" Update settings on {plan.ClassUpdates.Count:N0} existing classes.";
            if (includeHistory)
                status.Text += $" Add {eventPlan.Count:N0} events, {eventPlan.Sum(e => e.Sessions.Count):N0} completed races and {eventPlan.Sum(e => e.Sessions.Sum(r => r.Laps.Count)):N0} scored laps.";
            if (trackPlan.Count > 0)
                status.Text += $" Restore the saved venue on {trackPlan.Count:N0} previously imported events.";
            if (plan.SkippedMissingAssignments > 0)
                status.Text += $" {plan.SkippedMissingAssignments:N0} assignments are missing a matching driver, class or existing assignment.";
            if (plan.Issues.Count > 0)
                status.Text += $" {plan.Issues.Count:N0} conflicts or skipped items.";
            commit.IsEnabled = plan.HasChanges || eventPlan.Count > 0 || trackPlan.Count > 0;
        }

        foreach (var (control, _, _, _) in toggles)
        {
            control.Checked += (_, _) => UpdatePreview();
            control.Unchecked += (_, _) => UpdatePreview();
        }

        eventList.SelectionChanged += (_, _) => UpdatePreview();

        async Task PreviewAsync(byte[]? password = null)
        {
            if(sourcePath is not { } path)return;
            source=null;plan=null;eventPlan=[];trackPlan=[];SetBusy(true);SetCategoryCounts(null);
            if (password is null)
            {
                unlockPanel.Visibility = Visibility.Collapsed;
                passwordBox.Clear();
            }
            status.Text=password is null ? "Reading backup…" : "Unlocking and reading backup…";
            sourceLabel.Text=Path.GetFileName(sourcePath)+(referencePath is null ? "" : $" • Reference: {Path.GetFileName(referencePath)}");
            try
            {
                source=await Task.Run(()=>password is null
                    ? LiveTimeBackupReader.ReadImport(path,referencePath)
                    : LiveTimeBackupReader.ReadEncryptedImport(path,password));
                unlockPanel.Visibility = Visibility.Collapsed;
                SetCategoryCounts(source);
            }
            catch (LiveTimeBackupPasswordRequiredException ex)
            {
                unlockPanel.Visibility = Visibility.Visible;
                status.Text = ex.Message;
            }
            catch(Exception ex) {status.Text=ex.Message;}
            finally
            {
                SetBusy(false);
                if (source is not null) UpdatePreview();
                if (unlockPanel.Visibility == Visibility.Visible) passwordBox.Focus();
            }
        }

        async Task UnlockAsync()
        {
            if (busy || sourcePath is null) return;
            using var secure = passwordBox.SecurePassword;
            if (secure.Length == 0) { status.Text = "Enter the backup password or unlock key."; return; }
            var pointer = Marshal.SecureStringToGlobalAllocUnicode(secure);
            var characters = new char[secure.Length];
            byte[]? key = null;
            try
            {
                Marshal.Copy(pointer, characters, 0, characters.Length);
                key = Encoding.UTF8.GetBytes(characters);
            }
            finally
            {
                Array.Clear(characters);
                Marshal.ZeroFreeGlobalAllocUnicode(pointer);
                passwordBox.Clear();
            }
            try { await PreviewAsync(key); }
            finally { if (key is not null) CryptographicOperations.ZeroMemory(key); }
        }
        unlock.Click += async (_, _) => await UnlockAsync();
        unlockFile.Click += async (_, _) =>
        {
            if (busy || sourcePath is null) return;
            var picker = new OpenFileDialog { Title = "Choose the unlock key for this backup",
                Filter = "LiveTime unlock key (*.livetime-key)|*.livetime-key|All files (*.*)|*.*" };
            if (picker.ShowDialog(dialog) != true) return;
            var key = new byte[99];
            try
            {
                using (var file = File.OpenRead(picker.FileName))
                {
                    if (file.Length != key.Length)
                        throw new InvalidDataException("Choose a valid LiveTime unlock key file.");
                    file.ReadExactly(key);
                }
                if (key[0] != (byte)'x' || key[1] != (byte)'\'' || key[^1] != (byte)'\'' ||
                    key.Skip(2).Take(96).Any(c => !Uri.IsHexDigit((char)c)))
                    throw new InvalidDataException("Choose a valid LiveTime unlock key file.");
                passwordBox.Clear();
                await PreviewAsync(key);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { status.Text = ex.Message; }
            finally { CryptographicOperations.ZeroMemory(key); }
        };
        passwordBox.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await UnlockAsync();
        };
        select.Click+=async (_,_)=>{if(Pick("Select a LiveTime database or backup") is { } path){sourcePath=path;referencePath=null;await PreviewAsync();}};
        reference.Click+=async (_,_)=>{if(Pick("Select an older backup from the same LiveTime installation") is { } path){referencePath=path;await PreviewAsync();}};
        commit.Click+=async (_,_) =>
        {
            if(plan is not { } additions || (!additions.HasChanges && eventPlan.Count == 0 && trackPlan.Count == 0) || !ApplicationUpdateAllowed)return;
            var importedEvents = eventPlan;
            var restoredTracks = trackPlan;
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
                foreach (var change in additions.ClassUpdates) change.After.ApplyTo(change.Class);
                foreach (var item in importedEvents) Events.Add(item);
                foreach (var link in restoredTracks) link.Event.Track = link.Track;
                foreach(var assignment in additions.Assignments)_driverAssignments.Add(assignment);
                foreach (var fill in additions.TagFills)
                {
                    var assignment = _driverAssignments.FirstOrDefault(a => a.Id == fill.AssignmentId);
                    if (assignment is null || !string.IsNullOrWhiteSpace(assignment.TransponderId))
                        throw new InvalidOperationException("An assignment changed after the preview. Reopen the import to preview it again.");
                    updated.Add((assignment, assignment.TransponderId));
                    assignment.TransponderId = fill.Transponders;
                }
                await CaptureAndSaveSettingsAsync(true);
            }
            catch(Exception ex)
            {
                if(applied)
                {
                    foreach(var driver in additions.Drivers)Drivers.Remove(driver);
                    foreach(var cls in additions.Classes)Classes.Remove(cls);
                    foreach (var change in additions.ClassUpdates) change.Before.ApplyTo(change.Class);
                    foreach (var item in importedEvents) Events.Remove(item);
                    foreach (var link in restoredTracks) link.Event.Track = null;
                    _settings.Events = Events.ToList();
                    foreach(var assignment in additions.Assignments)_driverAssignments.Remove(assignment);
                    foreach (var (assignment, previous) in updated) assignment.TransponderId = previous;
                    _settings.Drivers=Drivers.ToList();_settings.Classes=Classes.ToList();
                    _settings.DriverClassAssignments=_driverAssignments.ToList();
                }
                status.Text="Import did not complete: "+ex.Message;
                return;
            }
            finally {SetBusy(false);}
            ClassGrid.Items.Refresh();
            UpdateClassDetail(_editingClass);
            RefreshAssignmentRows();
            RefreshEventSetupCollections();
            TrackRecordsPanel.Refresh();
            UpdatePreview();
            status.Text=$"Imported {additions.Drivers.Count} drivers, {additions.Classes.Count} classes and {additions.Assignments.Count} assignments. Filled transponders on {additions.TagFills.Count} existing assignments. Updated {additions.ClassUpdates.Count} existing class settings. Added {importedEvents.Count} events and {importedEvents.Sum(e => e.Sessions.Count)} completed races. Restored tracks on {restoredTracks.Count} existing events.";
            FooterStatusText.Text="Selected LiveTime data imported.";
        };
        dialog.ShowDialog();
    }
}
