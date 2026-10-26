using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Text.Json;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;
namespace IMRCRaceControl;
public partial class MainWindow
{
    internal double PopoutZoom => Math.Clamp(_settings.PopoutScreenZoom, 50, 150) / 100;
    internal double OverallZoom => Math.Clamp(_settings.OverallPositionZoom, 50, 150) / 100;
    internal bool IncludePopoutHeader => _settings.PopoutZoomHeader;
    internal IReadOnlyList<string> SpectatorColumns => DashboardColumnCatalog.LockedColumns.Concat(
        DashboardColumnCatalog.OptionalColumns.Where(c => c != "MANUAL" && (_settings.SpectatorVisibleColumns ?? _settings.DashboardVisibleColumns).Contains(c))).ToArray();
    internal bool ShowSpectatorHeader => _settings.SpectatorShowHeader;
    internal bool ShowSpectatorClock => _settings.SpectatorShowClock;
    internal bool ShowSpectatorNext => _settings.SpectatorShowNextUp;
    internal bool ShowSpectatorOverall => _settings.WebShowOverallPosition;
    private void ApplyScreenZoom()
    {
        var z = Math.Clamp(_settings.MainScreenZoom,50,150)/100;
        MainScreenRoot.LayoutTransform = System.Windows.Media.Transform.Identity;
        StandingsGrid.LayoutTransform = new System.Windows.Media.ScaleTransform(z,z);
        DashboardRaceHeader.LayoutTransform = new System.Windows.Media.ScaleTransform(_settings.MainZoomHeader ? z : 1, _settings.MainZoomHeader ? z : 1);
        if (DashboardZoom is not null) DashboardZoom.Value=z*100;
        if (DashboardHeaderZoom is not null) DashboardHeaderZoom.IsChecked = _settings.MainZoomHeader;
    }
    private string ApplyWebDisplayPreferences(string json)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        node["columns"] = JsonSerializer.SerializeToNode(SpectatorColumns);
        node["showOverall"] = ShowSpectatorOverall;
        node["showHeader"] = ShowSpectatorHeader;
        node["showClock"] = ShowSpectatorClock;
        node["showNext"] = ShowSpectatorNext;
        return node.ToJsonString();
    }

    private LocalScoreboardServer? _localBoard;
    private void LocalBoard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _localBoard ??= new LocalScoreboardServer(async () => await Dispatcher.InvokeAsync(BuildWebSnapshot));
            var addresses = LocalScoreboardServer.NetworkUrls();
            FooterStatusText.Text = "Browser scoreboard: " + (addresses.FirstOrDefault() ?? _localBoard.Url);
            var info = new Window { Owner=this, Title="Network scoreboard", Width=530, SizeToContent=SizeToContent.Height, WindowStartupLocation=WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin=new Thickness(18) };
            panel.Children.Add(new TextBlock { Text="Open an address below on a device connected to the same network:", TextWrapping=TextWrapping.Wrap });
            var links = new TextBox { Text=addresses.Length>0?string.Join(Environment.NewLine,addresses):"No active IPv4 network address found. Connect this PC to Wi-Fi or Ethernet and click WEB again.", IsReadOnly=true, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,10,0,10) };
            panel.Children.Add(links);
            var copy = new Button { Content="COPY NETWORK ADDRESSES", IsEnabled=addresses.Length>0 };
            copy.Click += (_,_) => { try { Clipboard.SetText(string.Join(Environment.NewLine,addresses)); } catch(Exception ex) { MessageBox.Show(info,ex.Message); } };
            panel.Children.Add(copy);
            var open = new Button { Content="OPEN ON THIS COMPUTER" };
            open.Click += (_,_) => { try { Process.Start(new ProcessStartInfo(_localBoard.Url) { UseShellExecute=true }); } catch(Exception ex) { MessageBox.Show(info,ex.Message); } };
            panel.Children.Add(open);
            panel.Children.Add(new TextBlock { Text="Keep Race Control open. Allow the app through Windows Firewall on your Private network if prompted. Guest Wi-Fi may block devices from reaching each other. This scoreboard is view-only.", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,12,0,0) });
            info.Content=panel; info.ShowDialog();
        }
        catch(Exception ex) { MessageBox.Show(this, ex.Message, "Browser scoreboard"); }
    }
    private string BuildWebSnapshot()
    {
        if (_finishedSpectatorFrame is not null) return ApplyWebDisplayPreferences(_finishedSpectatorFrame.WebJson);
        string Color(string key) => (TryFindResource(key) as System.Windows.Media.SolidColorBrush)?.Color.ToString() is string c ? "#"+c[^6..] : "#ffffff";
        var keys = new[]{"AccentDarkBrush","DashboardBackgroundBrush","DashboardPanelBrush","DashboardRowBrush","DashboardAlternateRowBrush","DashboardTextBrush","DashboardCheckInGlowBrush","DashboardMinimumLapGlowBrush","DriverCrossingBrush","DashboardFastestLapHighlightBrush","DashboardFastestLapHighlightTextBrush","DashboardNewBestLapHighlightBrush","DashboardNewBestLapHighlightTextBrush","FinishedDriverBrush","FinishedDriverContrastBrush","DashboardPositionUpBrush","DashboardPositionDownBrush","DashboardPositionSameBrush"};
        return ApplyWebDisplayPreferences(JsonSerializer.Serialize(new {
            title=SpectatorIsPractice?"PRACTICE":SpectatorRaceHeader, subtitle=SpectatorRaceSubheader,
            clock=SpectatorIsPractice?"":SpectatorMasterClock, state=SpectatorRaceState, practice=SpectatorIsPractice,
            next=GetNextRacePreview(), colors=keys.ToDictionary(k=>k,Color),
            crossingStyle=_settings.DriverCrossingStyle, finishedStyle=_settings.FinishedDriverStyle,
            showOverall=_settings.WebShowOverallPosition, projectionInfo=SpectatorProjectionText,
            columns=DashboardColumnCatalog.LockedColumns.Concat(_settings.DashboardColumnOrder.Where(c=>c!="MANUAL" && _settings.DashboardVisibleColumns.Contains(c))).ToArray(),
            rows=Standings.ToArray(), projections=RoundProjectionRows.ToArray(),
            practices=VisiblePracticeCards.Select(d=>new {name=d.DriverName,laps=d.LapCount,best=d.BestLapDisplay,last=d.LastLapDisplay,average=d.AverageLapDisplay,stats=d.VisibleStats.Select(v=>new {label=v.Label,value=v.Value}).ToArray()}).ToArray()
        }));
    }

    private async void RoundFormat_Click(object sender, RoutedEventArgs e)
    {
        var current = _loadedSession;
        if (current is null || _activeEvent is null) { MessageBox.Show(this,"Load a race first."); return; }
        var sessions = _activeEvent.Sessions.Where(s => s.ClassId == current.ClassId && s.SessionType == current.SessionType &&
            (s.SessionType == SessionType.Race ? s.MainLeg == current.MainLeg : s.Round == current.Round)).ToList();
        if (current.ProgressStatus == EventSessionStatus.Completed)
        { MessageBox.Show(this,"This race is completed. Load an unfinished race to adjust its format."); return; }
        var raceEvent = _activeEvent;
        var cls = Classes.First(c => c.Id == current.ClassId);
        var dialog = new Window { Owner=this, Title=$"{cls.Name} • {current.RoundLabel} format override", Width=540, SizeToContent=SizeToContent.Height, WindowStartupLocation=WindowStartupLocation.CenterOwner, ResizeMode=ResizeMode.NoResize };
        var panel = new StackPanel { Margin=new Thickness(18) };
        var format = new ComboBox { ItemsSource=new[]{"Timed race","Lapped race"}, SelectedIndex=current.RaceFormat==RaceFormat.Laps?1:0 };
        var caption = new TextBlock();
        var value = new TextBox();
        void UpdateInput() { caption.Text=format.SelectedIndex==0?"Minutes":"Target laps"; value.Text=format.SelectedIndex==0?(current.RaceMinutesOverride??cls.MinutesFor(current.SessionType)).ToString("0.###"):current.TargetLaps.ToString(); }
        format.SelectionChanged += (_,_)=>UpdateInput(); UpdateInput();
        panel.Children.Add(new TextBlock { Text="Applies to this race and unstarted races in this class/round. Recorded laps are retained. Already-finished drivers and completed races stay finished. A shorter target takes effect at each unfinished driver’s next crossing.", TextWrapping=TextWrapping.Wrap });
        panel.Children.Add(format); panel.Children.Add(caption); panel.Children.Add(value);
        var minimum = new TextBox { Text=(current.MinimumLapSecondsOverride ?? cls.MinimumLapSeconds).ToString("0.###") };
        var styles = Enum.GetValues<StartStyle>();
        var clockStyle = new ComboBox { ItemsSource=styles.Select(s => s.ToDisplay()).ToList(), SelectedIndex=Array.IndexOf(styles,current.StartStyle) };
        panel.Children.Add(new TextBlock { Text="Minimum lap time (seconds)", Margin=new Thickness(0,10,0,0) });
        panel.Children.Add(minimum);
        panel.Children.Add(new TextBlock { Text="Clock / start style", Margin=new Thickness(0,10,0,0) });
        panel.Children.Add(clockStyle);
        panel.Children.Add(new TextBlock { Text="Minimum lap changes apply to future crossings. Tone uses the original start signal; leader uses its recorded crossing (or the existing race start). Individual keeps clocks already started and starts waiting drivers at their next crossing. Recorded laps stay unchanged.", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,0) });
        var apply = new Button { Content="APPLY TO THIS ROUND", Margin=new Thickness(0,12,0,0) };
        double minutes=0, minimumSeconds=0; int laps=0;
        apply.Click += (_,_) => {
            bool valid = format.SelectedIndex==0 ? double.TryParse(value.Text,out minutes)&&double.IsFinite(minutes)&&minutes>0&&minutes<=1440 : int.TryParse(value.Text,out laps)&&laps>=1&&laps<=10000;
            if (!valid) { MessageBox.Show(dialog,"Enter minutes from above 0 to 1440, or 1–10000 laps."); return; }
            if (!double.TryParse(minimum.Text,out minimumSeconds) || !double.IsFinite(minimumSeconds) || minimumSeconds<0 || minimumSeconds>86400 || clockStyle.SelectedIndex<0)
            { MessageBox.Show(dialog,"Enter a minimum lap time from 0 to 86400 seconds and select a clock style."); return; }
            dialog.DialogResult=true;
        };
        panel.Children.Add(apply); dialog.Content=panel;
        if(dialog.ShowDialog()!=true) return;
        // Timing continues while this dialog is open; recheck before changing anything.
        if (_loadedSession != current || _activeEvent != raceEvent || current.ProgressStatus == EventSessionStatus.Completed)
        { MessageBox.Show(this,"The race changed or finished while the format window was open. No changes were applied."); return; }
        var live = _raceEngine.State is RaceState.Armed or RaceState.Running or RaceState.TimeExpired;
        var selectedFormat = format.SelectedIndex == 0 ? RaceFormat.Timed : RaceFormat.Laps;
        if (live)
            _raceEngine.UpdateLiveFormat(selectedFormat,
                selectedFormat == RaceFormat.Timed ? TimeSpan.FromMinutes(minutes) : _raceEngine.Duration,
                selectedFormat == RaceFormat.Laps ? laps : _raceEngine.TargetLaps, DateTimeOffset.Now,
                TimeSpan.FromSeconds(minimumSeconds), styles[clockStyle.SelectedIndex]);
        foreach(var s in sessions.Where(s => s == current || s.ProgressStatus is EventSessionStatus.Planned or EventSessionStatus.Loaded))
        { s.MinimumLapSecondsOverride=minimumSeconds; s.StartStyle=styles[clockStyle.SelectedIndex]; s.RaceFormat=selectedFormat; if(selectedFormat==RaceFormat.Timed)s.RaceMinutesOverride=minutes;else s.TargetLaps=laps; }
        if (!live) LoadSessionIntoRaceControl(current,saveSettings:false);
        else
        {
            RaceMinutesText.Text = (_loadedSession.RaceMinutesOverride ?? cls.MinutesFor(current.SessionType)).ToString("0.###");
            MinimumLapText.Text = minimumSeconds.ToString("0.###");
            StartStyleText.Text = current.StartStyle.ToDisplay();
            RefreshRaceUi(DateTimeOffset.Now);
            RefreshRaceDayFlow();
        }
        await CaptureAndSaveSettingsAsync();
        FooterStatusText.Text=$"Format override applied to {cls.Name} {current.RoundLabel}. Class defaults unchanged.";
    }
}
