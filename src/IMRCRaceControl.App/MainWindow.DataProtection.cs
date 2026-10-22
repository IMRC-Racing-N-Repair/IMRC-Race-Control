using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using IMRCRaceControl.Services;
namespace IMRCRaceControl;
public partial class MainWindow
{
    private void DataProtection_Click(object sender,RoutedEventArgs e)
    {
        if (!ApplicationUpdateAllowed)
        { MessageBox.Show(this,"Finish the race or practice session before managing database backups.","Saved data"); return; }
        var dialog = new Window { Owner=this,Title="Race data and backups",Width=660,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin=new Thickness(20) };
        panel.Children.Add(new TextBlock { Text="SAVED RACE DATA",FontSize=22,FontWeight=FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text="Events, drivers, settings and practice history are stored outside the application installation. Repair/reinstall must not remove this folder.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10) });
        panel.Children.Add(new TextBox { Text=ApplicationDataPaths.DataDirectory,IsReadOnly=true });
        panel.Children.Add(new TextBlock { Text="DATABASE BACKUPS",FontWeight=FontWeights.Bold,Margin=new Thickness(0,14,0,5) });
        panel.Children.Add(new TextBox { Text=ApplicationDataPaths.BackupDirectory,IsReadOnly=true });
        var actions = new WrapPanel { Margin=new Thickness(0,14,0,0) };
        var backup = new Button { Content="BACK UP NOW" };
        backup.Click += async (_,_) => {
            backup.IsEnabled=false;
            try { await CaptureAndSaveSettingsAsync(true); var path=await DataProtectionService.BackupAsync(ApplicationDataPaths.DatabasePath,ApplicationDataPaths.BackupDirectory,true); MessageBox.Show(dialog,"Backup created:\n"+path,"Database backup"); }
            catch(Exception ex) { MessageBox.Show(dialog,ex.Message,"Backup could not be completed"); }
            finally { backup.IsEnabled=true; }
        };
        var open = new Button { Content="OPEN BACKUPS" };
        open.Click += (_,_) => { Directory.CreateDirectory(ApplicationDataPaths.BackupDirectory); Process.Start(new ProcessStartInfo(ApplicationDataPaths.BackupDirectory) { UseShellExecute=true }); };
        var restore = new Button { Content="RESTORE DATABASE…" };
        restore.Click += async (_,_) => {
            var picker=new OpenFileDialog { Title="Select a Race Control database backup",Filter="Race Control database (*.db)|*.db",InitialDirectory=ApplicationDataPaths.BackupDirectory };
            if (picker.ShowDialog(dialog)!=true) return;
            if (MessageBox.Show(dialog,"Restore this database on the next launch? The current database will also be backed up before it is replaced.","Restore saved race data",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) return;
            try { await CaptureAndSaveSettingsAsync(true); await DataProtectionService.QueueRestoreAsync(picker.FileName,ApplicationDataPaths.BackupDirectory); MessageBox.Show(dialog,"Restore queued. Close and reopen Race Control to apply it.","Database restore"); }
            catch(Exception ex) { MessageBox.Show(dialog,ex.Message,"Database was not restored"); }
        };
        actions.Children.Add(backup); actions.Children.Add(open); actions.Children.Add(restore); panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text="If an older repair already removed the old database, a surviving database or backup is needed to recover those records. An installer cannot recreate deleted results.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0) });
        dialog.Content=panel; dialog.ShowDialog();
    }
}
