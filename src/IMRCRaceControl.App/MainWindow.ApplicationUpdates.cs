using System.Windows;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;
namespace IMRCRaceControl;
public partial class MainWindow
{
    private bool _applicationUpdateBusy;
    private bool ApplicationUpdateAllowed => !_practiceActive && !_resultAutoSaveInProgress &&
        _raceEngine.State is not (RaceState.Armed or RaceState.Running or RaceState.TimeExpired);
    internal async Task CheckApplicationUpdatesAsync(bool manual)
    {
        if (_applicationUpdateBusy) return;
        if (!ApplicationUpdateAllowed)
        {
            if (manual) MessageBox.Show(this,"Finish the race or practice session before updating.","Application updates");
            return;
        }
        _applicationUpdateBusy = true;
        try
        {
            var manager = ApplicationUpdateService.CreateManager();
            if (manager is null || !manager.IsInstalled)
            {
                if (manual) MessageBox.Show(this,"Online updates are available in the installed GitHub release build. Install using its RaceControl-Installer.exe first.","Application updates");
                return;
            }
            FooterStatusText.Text = "Checking for application updates…";
            var update = await manager.CheckForUpdatesAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (!ApplicationUpdateAllowed || !IsLoaded) return;
            if (update is null)
            {
                FooterStatusText.Text = "Race Control is up to date.";
                if (manual) MessageBox.Show(this,"You are using the latest published version.","Application updates");
                return;
            }
            if (MessageBox.Show(this,$"Race Control {update.TargetFullRelease.Version} is available. Download, install and restart now?\n\nChoose No to continue with this version.","Application update",MessageBoxButton.YesNo,MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            if (!ApplicationUpdateAllowed) return;
            // Prevent a new race from starting during the download/restart.
            IsEnabled = false;
            try
            {
                await CaptureAndSaveSettingsAsync(reportFailureToCaller: true);
                await DataProtectionService.BackupAsync(ApplicationDataPaths.DatabasePath,ApplicationDataPaths.BackupDirectory,force: true);
                FooterStatusText.Text = "Downloading application update…";
                using var downloadTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
                await manager.DownloadUpdatesAsync(update,
                    progress => Dispatcher.BeginInvoke(new Action(() => FooterStatusText.Text = $"Downloading application update… {progress}%")),
                    downloadTimeout.Token);
                manager.ApplyUpdatesAndRestart(update);
            }
            finally { IsEnabled = true; }
        }
        catch (Exception ex)
        {
            AppCrashLogger.Log("Application update",ex);
            FooterStatusText.Text = "Update check/download unavailable. You can keep using this version.";
            if (manual) MessageBox.Show(this,"The update could not be completed. Your installed version is still available.\n\n"+ex.Message,"Application updates",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        finally { _applicationUpdateBusy = false; }
    }
}
