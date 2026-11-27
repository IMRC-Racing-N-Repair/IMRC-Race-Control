using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Automation;
using IMRCRaceControl.Controls;
using IMRCRaceControl.Models;
using IMRCRaceControl.Core;
using IMRCRaceControl.Services;
namespace IMRCRaceControl;
public partial class MainWindow
{
    private readonly DispatcherTimer _onlineTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private OnlinePublisher? _onlinePublisher;
    private string _onlineScope = "";
    private bool _onlineBusy;
    private readonly CancellationTokenSource _onlineCancel = new();
    private readonly OnlineUploadSchedule _onlineSchedule = new();
    private WebsiteConnectionPanel? _onlinePanel;
    private bool _onlineConnected, _onlineTransferring, _onlineConnecting, _onlineConnectionFailed;
    private WebsiteConnectionState? _headerWebsiteState;
    private Window OnlineDialogOwner => _onlinePanel is not null ? Window.GetWindow(_onlinePanel) ?? this : this;
    private DateTimeOffset _nextPracticeArchiveAttempt;
    private string PracticeArchiveScope(OnlinePublishingSettings s)=>s.BaseUrl.TrimEnd('/')+"|"+s.OnlineTrackId+"|"+_settings.Track.Id;
    private bool PracticeArchiveEnabled(OnlinePublishingSettings s)=>s.PracticeArchiveScope==PracticeArchiveScope(s);
    private void InitializeOnlinePublishing()
    {
        _onlinePanel = new WebsiteConnectionPanel();
        _onlinePanel.OnlineApplyButton.Click += ApplyOnlineData_Click;
        _onlinePanel.OnlineResetMeterButton.Click += ResetOnlineMeter_Click;
        _onlinePanel.OnlineEnableArchiveButton.Click += EnablePracticeArchive_Click;
        _onlinePanel.OnlinePauseArchiveButton.Click += PausePracticeArchive_Click;
        _onlinePanel.OnlineRegisterButton.Click += RegisterOnlineTrack_Click;
        _onlinePanel.OnlineSaveButton.Click += SaveOnlineConnection_Click;
        _onlinePanel.OnlinePublishButton.Click += PublishOnlineNow_Click;
        _onlinePanel.OnlinePauseButton.Click += DisableOnlinePublishing_Click;
        _onlinePanel.OnlineOpenTrackButton.Click += OpenOnlineTrack_Click;
        ReadSavedOnlineSettingsIntoPanel();
        UpdateOnlineMeter();
        UpdatePracticeArchiveStatus();
        _onlinePanel.OnlineStatusText.Text = _settings.OnlinePublishing.Enabled ? "Publishing enabled. First synchronization will start shortly." : "Publishing off. Local scoring is independent of the website.";
        UpdateWebsiteIndicator();
        _onlineTimer.Tick += async (_, _) => await PublishOnlineAsync();
        _onlineTimer.Start();
    }
    private void ReadSavedOnlineSettingsIntoPanel()
    {
        if (_onlinePanel is null) return;
        var saved = _settings.OnlinePublishing;
        _onlinePanel.OnlineBaseUrlText.Text = saved.BaseUrl;
        _onlinePanel.OnlineTrackIdText.Text = string.IsNullOrWhiteSpace(saved.OnlineTrackId) ? _settings.Track.Id : saved.OnlineTrackId;
        _onlinePanel.OnlineEnabledCheck.IsChecked = saved.Enabled;
        _onlinePanel.OnlineLimitText.Text = saved.UploadLimitMb.ToString();
        _onlinePanel.OnlineIntervalText.Text = saved.UploadIntervalSeconds.ToString();
        _onlinePanel.OnlineCompletedOnlyCheck.IsChecked = saved.CompletedRacesOnly;
        _onlinePanel.OnlineUploadKeyBox.Clear();
    }
    private bool WebsiteLimitBlocked()
    {
        // A budget exactly exhausted is paused even before the next request.
        var pub = _onlinePublisher;
        return pub is not null && (pub.LimitBlocked || pub.UploadLimitBytes > 0 && pub.UploadedBytes >= pub.UploadLimitBytes);
    }
    private void UpdateWebsiteIndicator()
    {
        var settings = _settings.OnlinePublishing;
        var configured = !string.IsNullOrWhiteSpace(settings.OnlineTrackId) && !string.IsNullOrWhiteSpace(settings.ProtectedUploadKey);
        var limited = WebsiteLimitBlocked();
        var state = WebsiteConnectionIndicator.Resolve(configured, settings.Enabled, _onlineConnected, _onlineTransferring, limited, _onlineConnecting, _onlineConnectionFailed);
        var status = state switch
        {
            WebsiteConnectionState.Uploading => _onlineConnecting ? "Connecting to IMRC Live…" : "Uploading to IMRC Live…",
            WebsiteConnectionState.Ready => "Connected • " + UploadModeDescription(settings),
            WebsiteConnectionState.Paused => limited ? "Publishing paused • data limit reached" : "Publishing paused",
            _ => configured ? "Not connected • waiting for a successful connection" : "Not connected • open Website Connection"
        };
        HeaderWebsiteButton.ToolTip = status + " — click to open Website Connection.";
        AutomationProperties.SetName(HeaderWebsiteButton, "Website connection: " + status);
        if (_headerWebsiteState == state) return;
        _headerWebsiteState = state;
        HeaderWebsiteIcon.Foreground = new SolidColorBrush(state switch
        {
            WebsiteConnectionState.Ready or WebsiteConnectionState.Uploading => Color.FromRgb(53, 199, 111),
            WebsiteConnectionState.Paused => Color.FromRgb(255, 149, 50),
            _ => Color.FromRgb(229, 59, 59)
        });
        HeaderWebsiteIcon.BeginAnimation(UIElement.OpacityProperty, null);
        HeaderWebsiteIcon.Opacity = 1;
        if (state == WebsiteConnectionState.Uploading)
            HeaderWebsiteIcon.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(500))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }
    private void SetOnlineTransferring(bool transferring)
    {
        _onlineTransferring = transferring;
        UpdateWebsiteIndicator();
    }
    private void RequestCompletedRaceUpload()
    {
        if (_onlinePanel is null || !_settings.OnlinePublishing.Enabled || !_settings.OnlinePublishing.CompletedRacesOnly || _closing) return;
        _onlineSchedule.RequestSnapshot();
        // The one-second dispatcher poll picks up the completed result without
        // adding snapshot or HTTP work to the race-finalization path.
    }
    private OnlinePublisher GetPublisher(OnlinePublishingSettings settings)
    {
        var scope = settings.BaseUrl + "|" + settings.OnlineTrackId + "|" + _settings.Track.Id;
        if (_onlinePublisher is null || scope != _onlineScope)
        {
            _onlinePublisher?.Dispose();
            _onlinePublisher = new OnlinePublisher(Path.Combine(ApplicationDataPaths.DataDirectory, "OnlineOutbox"), scope);
            _onlineScope = scope;
            _onlineConnected = false;
            _onlineConnectionFailed = false;
        }
        _onlinePublisher.UploadLimitBytes = checked(Math.Max(0, settings.UploadLimitMb) * 1_000_000L);
        return _onlinePublisher;
    }
    private async void SaveOnlineConnection_Click(object sender, RoutedEventArgs e)
    {
        if (_onlineBusy) { _onlinePanel!.OnlineStatusText.Text = "Please wait for the current connection attempt."; return; }
        var candidate = new OnlinePublishingSettings { BaseUrl = _onlinePanel!.OnlineBaseUrlText.Text.Trim().TrimEnd('/'),
            OnlineTrackId = _onlinePanel!.OnlineTrackIdText.Text.Trim(), Enabled = _onlinePanel!.OnlineEnabledCheck.IsChecked == true,
            ProtectedUploadKey = _settings.OnlinePublishing.ProtectedUploadKey, RegistrationTrackUuid = _settings.Track.Id,
            CompletedRacesOnly = _settings.OnlinePublishing.CompletedRacesOnly, UploadLimitMb = _settings.OnlinePublishing.UploadLimitMb,
            UploadIntervalSeconds = _settings.OnlinePublishing.UploadIntervalSeconds,
            PracticeArchiveScope=_settings.OnlinePublishing.PracticeArchiveScope };
        var saved = false;
        try
        {
            ReadOnlineDataOptions(candidate);
            OnlinePublisher.ValidateBaseUrl(candidate.BaseUrl);
            if (string.IsNullOrWhiteSpace(candidate.OnlineTrackId)) throw new ArgumentException("Copy the online track ID from Track manager on the website.");
            if (_onlinePanel!.OnlineUploadKeyBox.Password.Length > 0) candidate.ProtectedUploadKey = OnlineSecret.Protect(_onlinePanel!.OnlineUploadKeyBox.Password.Trim());
            if (candidate.ProtectedUploadKey.Length == 0) throw new ArgumentException("Paste the upload key generated by the website's track manager.");
            if (string.IsNullOrWhiteSpace(_settings.Track.Name)) throw new ArgumentException("Save your local track information first.");
            _onlineBusy = true;
            _onlineConnecting = true;
            UpdateWebsiteIndicator();
            var key = OnlineSecret.Unprotect(candidate.ProtectedUploadKey);
            _onlinePanel!.OnlineStatusText.Text = "Testing secure connection…";
            var name = await GetPublisher(candidate).TestAsync(candidate.BaseUrl, candidate.OnlineTrackId, key, _settings.Track.Id, _onlineCancel.Token);
            if (MessageBox.Show(OnlineDialogOwner, $"Connect local track '{_settings.Track.Name}' to online track '{name}'?\n\n" +
                "When publishing is enabled, associated events, lineups, racer display names, laps and Series points will be public. Driver UUIDs, ratings and contact details are not uploaded. Transponders are included only if you separately enable the public practice archive.",
                "Confirm track connection", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var previous = _settings.OnlinePublishing;
            _settings.OnlinePublishing = candidate;
            try { await CaptureAndSaveSettingsAsync(reportFailureToCaller: true); }
            catch { _settings.OnlinePublishing = previous; throw; }
            _onlinePanel!.OnlineUploadKeyBox.Clear(); _onlineSchedule.Reset(); _onlineConnected = true; _onlineConnectionFailed = false;
            _onlinePanel!.OnlineStatusText.Text = $"Connected to {name}. " + (candidate.Enabled ? "Publishing is enabled." : "Publishing is off.");
            saved = true;
        }
        catch (OperationCanceledException) { if (_onlineConnecting) { _onlineConnected = false; _onlineConnectionFailed = true; } _onlinePanel!.OnlineStatusText.Text = "Connection timed out or was cancelled. Try again."; }
        catch (Exception ex) { if (_onlineConnecting) { _onlineConnected = false; _onlineConnectionFailed = true; } _onlinePanel!.OnlineStatusText.Text = "Connection not saved: " + ex.Message; }
        finally { _onlineBusy = false; _onlineConnecting = false; UpdateOnlineMeter(); UpdatePracticeArchiveStatus(); UpdateWebsiteIndicator(); }
        if (saved && _settings.OnlinePublishing.Enabled) await PublishOnlineAsync(true);
    }
    private async void DisableOnlinePublishing_Click(object sender, RoutedEventArgs e)
    {
        if (_onlineConnecting) { _onlinePanel!.OnlineStatusText.Text = "Wait for the connection test or registration to finish."; return; }
        _settings.OnlinePublishing.Enabled = false; _onlinePanel!.OnlineEnabledCheck.IsChecked = false;
        _onlineConnectionFailed = false;
        UpdateWebsiteIndicator();
        try
        {
            await CaptureAndSaveSettingsAsync(reportFailureToCaller: true);
            _onlinePanel!.OnlineStatusText.Text = "Publishing paused. Any current request may finish; stored public results remain available.";
        }
        catch (Exception ex) { _onlinePanel!.OnlineStatusText.Text = "Paused for this run, but the setting could not be saved: " + ex.Message; }
    }
    private async void PublishOnlineNow_Click(object sender, RoutedEventArgs e) => await PublishOnlineAsync(true);
    private async void RegisterOnlineTrack_Click(object sender, RoutedEventArgs e)
    {
        if (_onlineBusy) { _onlinePanel!.OnlineStatusText.Text = "Please wait for the current connection attempt."; return; }
        _onlineBusy = true;
        var registered = false;
        try
        {
            var baseUrl = OnlinePublisher.ValidateBaseUrl(_onlinePanel!.OnlineBaseUrlText.Text).GetLeftPart(UriPartial.Authority);
            OnlineTrackRegistration.BuildBody(_settings.Track); // Validate before changing saved credentials.
            var previous = _settings.OnlinePublishing;
            var sameTrack = previous.BaseUrl.TrimEnd('/') == baseUrl &&
                (previous.RegistrationTrackUuid == _settings.Track.Id ||
                 (previous.RegistrationTrackUuid.Length == 0 && previous.OnlineTrackId.Length > 0));
            var candidate = new OnlinePublishingSettings {
                BaseUrl = baseUrl, OnlineTrackId = sameTrack && previous.OnlineTrackId.Length > 0 ? previous.OnlineTrackId : _settings.Track.Id,
                RegistrationTrackUuid = _settings.Track.Id, Enabled = false,
                CompletedRacesOnly = previous.CompletedRacesOnly, UploadLimitMb = previous.UploadLimitMb,
                UploadIntervalSeconds = previous.UploadIntervalSeconds,
                PracticeArchiveScope=previous.PracticeArchiveScope,
                ProtectedUploadKey = sameTrack && previous.ProtectedUploadKey.Length > 0 ? previous.ProtectedUploadKey :
                    OnlineSecret.Protect(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)))
            };
            ReadOnlineDataOptions(candidate);
            // Save BEFORE sending: a lost response/restart must retry using the same private key.
            _onlineConnecting = true; UpdateWebsiteIndicator();
            _settings.OnlinePublishing = candidate;
            try { await CaptureAndSaveSettingsAsync(reportFailureToCaller: true); }
            catch { _settings.OnlinePublishing = previous; throw; }
            _onlinePanel!.OnlineEnabledCheck.IsChecked = false;
            _onlinePanel!.OnlineStatusText.Text = "Registering this track and connecting publishing…";
            var result = await GetPublisher(candidate).RegisterAsync(baseUrl, _settings.Track,
                OnlineSecret.Unprotect(candidate.ProtectedUploadKey), _onlineCancel.Token);
            candidate.OnlineTrackId = result.Id;
            candidate.Enabled = true;
            try { await CaptureAndSaveSettingsAsync(reportFailureToCaller: true); }
            catch { candidate.Enabled = false; throw; }
            _onlinePanel!.OnlineTrackIdText.Text = result.Id; _onlinePanel!.OnlineBaseUrlText.Text = baseUrl;
            _onlinePanel!.OnlineEnabledCheck.IsChecked = true; _onlinePanel!.OnlineUploadKeyBox.Clear();
            _onlineSchedule.Reset(); _onlineConnected = true; _onlineConnectionFailed = false;
            _onlinePanel!.OnlineStatusText.Text = $"{result.Name} is registered. Publishing is enabled; no sign-in is needed.";
            registered = true;
        }
        catch (OperationCanceledException) { if (_onlineConnecting) { _onlineConnected = false; _onlineConnectionFailed = true; } _onlinePanel!.OnlineStatusText.Text = "Registration timed out. Click REGISTER TRACK ONLINE again; the saved key makes retries safe."; }
        catch (Exception ex) { if (_onlineConnecting) { _onlineConnected = false; _onlineConnectionFailed = true; } _onlinePanel!.OnlineStatusText.Text = "Registration not completed: " + ex.Message; }
        finally { _onlineBusy = false; _onlineConnecting = false; UpdateOnlineMeter(); UpdatePracticeArchiveStatus(); UpdateWebsiteIndicator(); }
        if (registered) await PublishOnlineAsync(true);
    }
    private void OpenOnlineTrack_Click(object sender, RoutedEventArgs e)
    {
        try { var uri = OnlinePublisher.ValidateBaseUrl(_settings.OnlinePublishing.BaseUrl);
            Process.Start(new ProcessStartInfo(new Uri(uri, "?track=" + Uri.EscapeDataString(_settings.OnlinePublishing.OnlineTrackId)).ToString()) { UseShellExecute = true }); }
        catch (Exception ex) { _onlinePanel!.OnlineStatusText.Text = ex.Message; }
    }
    private async Task PublishOnlineAsync(bool force = false)
    {
        if (_onlinePanel is null || _onlineBusy || _closing || !_settings.OnlinePublishing.Enabled) return;
        var settings = _settings.OnlinePublishing;
        var now = DateTimeOffset.UtcNow;
        var practiceActive = _practiceActive;
        var snapshotDue = _onlineSchedule.SnapshotDue(settings, now, force, practiceActive);
        var archiveDue = PracticeArchiveEnabled(settings) &&
            (force || now >= _nextPracticeArchiveAttempt && !_onlineSchedule.WaitingForRetry(now));
        if ((!snapshotDue && !archiveDue) || !force && WebsiteLimitBlocked()) return;
        _onlineBusy = true;
        try
        {
            var publisher = GetPublisher(settings);
            var key = OnlineSecret.Unprotect(settings.ProtectedUploadKey);
            if (snapshotDue)
            {
                _onlineSchedule.Started(settings, now, practiceActive);
                var capture = OnlineSnapshotBuilder.Capture(_settings.Track, Events, SeriesDefinitions, Drivers, Classes,
                    _activeEvent?.Id, _loadedSession?.Id, _raceEngine.State, _raceEngine.GetStandings(DateTimeOffset.Now),
                    _raceEngine.Laps, !_recallMode && !_practiceActive && !_raceEngine.PracticeMode, settings.CompletedRacesOnly, _raceEngine.TimingSettings);
                capture = capture with { Practice = OnlineSnapshotBuilder.CapturePractice(practiceActive, PracticeCards, now) };
                await publisher.CaptureAsync(capture, _settings.Track.Id, force, settings.CompletedRacesOnly);
                if (!settings.Enabled) return;
                if (publisher.PendingCount > 0)
                {
                    SetOnlineTransferring(true);
                    await publisher.FlushAsync(settings.BaseUrl, settings.OnlineTrackId, key, _onlineCancel.Token, () => _settings.OnlinePublishing.Enabled);
                    if (!settings.Enabled) return;
                    _onlineConnected = true; _onlineConnectionFailed = false;
                    _onlinePanel.OnlineStatusText.Text = $"Published at {DateTime.Now:T}. {capture.Documents.Count} event/Series records checked. " +
                        $"{capture.ExcludedEvents} events excluded (not assigned to this track); {capture.ExcludedSeries} multi-track Series excluded.";
                }
                else if (!_onlineConnected)
                {
                    // After an app restart, an unchanged results-only outbox has
                    // no traffic. Verify the configured connection once, not every poll.
                    SetOnlineTransferring(true);
                    await publisher.TestAsync(settings.BaseUrl, settings.OnlineTrackId, key, _settings.Track.Id, _onlineCancel.Token);
                    if (!settings.Enabled) return;
                    _onlineConnected = true; _onlineConnectionFailed = false;
                    _onlinePanel.OnlineStatusText.Text = "Connected. " + UploadModeDescription(settings);
                }
                _onlineSchedule.Succeeded();
                SetOnlineTransferring(false);
            }
            if (settings.Enabled && archiveDue && !WebsiteLimitBlocked())
                await PublishPracticeArchiveAsync(settings,publisher,key);
        }
        catch (OnlineUploadLimitException ex)
        {
            _onlinePanel.OnlineStatusText.Text = ex.Message + " Local scoring continues; results remain saved.";
        }
        catch (Exception ex)
        {
            _onlineConnected = false; _onlineConnectionFailed = true;
            var seconds = _onlineSchedule.Failed(DateTimeOffset.UtcNow);
            _onlinePanel.OnlineStatusText.Text = $"Online update pending ({_onlinePublisher?.PendingCount ?? 0}). {ex.Message} Retry in {seconds}s. Local scoring continues.";
        }
        finally
        {
            _onlineBusy = false;
            SetOnlineTransferring(false);
            if (!settings.Enabled) _onlinePanel.OnlineStatusText.Text = "Publishing paused. Pending updates are retained.";
            UpdateOnlineMeter();
            UpdateWebsiteIndicator();
        }
    }
    private void UpdatePracticeArchiveStatus()
    {
        if(_onlinePanel is null)return;
        _onlinePanel!.OnlinePracticeArchiveStatusText.Text=PracticeArchiveEnabled(_settings.OnlinePublishing)
            ? "Practice archive enabled for this connection. Saved sessions upload once, then new sessions upload automatically."
            : "Practice archive off. Existing live practice publishing is unchanged.";
    }
    private async void EnablePracticeArchive_Click(object sender,RoutedEventArgs e)
    {
        if(_onlineBusy){_onlinePanel!.OnlinePracticeArchiveStatusText.Text="An online operation is in progress. Try again.";return;}
        var settings=_settings.OnlinePublishing;
        if(!settings.Enabled || string.IsNullOrWhiteSpace(settings.OnlineTrackId) || string.IsNullOrWhiteSpace(settings.ProtectedUploadKey))
        {_onlinePanel!.OnlinePracticeArchiveStatusText.Text="Register/connect this track and enable publishing first.";return;}
        if(PracticeArchiveEnabled(settings)){UpdatePracticeArchiveStatus();return;}
        if(MessageBox.Show(OnlineDialogOwner,$"Enable public practice history for '{_settings.Track.Name}'?\n\nDriver display names, transponder numbers, session times and lap results will be public. Existing practice records without a track ID will be assigned to this track. Confirm only if those older sessions belong to this venue and you are authorized to publish this information. Future completed sessions upload automatically.",
            "Publish practice history",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        _onlineBusy=true;
        var previous=settings.PracticeArchiveScope;
        try
        {
            var assigned=await _practiceHistoryService.AssignLegacyTrackAsync(_settings.Track);
            settings.PracticeArchiveScope=PracticeArchiveScope(settings);
            await CaptureAndSaveSettingsAsync(reportFailureToCaller:true);
            _nextPracticeArchiveAttempt=DateTimeOffset.MinValue;
            _onlinePanel!.OnlinePracticeArchiveStatusText.Text=$"Enabled. {assigned} older sessions assigned to this venue. Backfill will upload with new sessions.";
        }
        catch(Exception ex){settings.PracticeArchiveScope=previous;_onlinePanel!.OnlinePracticeArchiveStatusText.Text="Practice archive not enabled: "+ex.Message;}
        finally{_onlineBusy=false;}
        if(PracticeArchiveEnabled(settings))await PublishOnlineAsync(true);
    }
    private async void PausePracticeArchive_Click(object sender,RoutedEventArgs e)
    {
        if(_onlineBusy){_onlinePanel!.OnlinePracticeArchiveStatusText.Text="An online operation is in progress. Try again.";return;}
        var settings=_settings.OnlinePublishing;
        var previous=settings.PracticeArchiveScope;
        settings.PracticeArchiveScope="";
        try{await CaptureAndSaveSettingsAsync(reportFailureToCaller:true);UpdatePracticeArchiveStatus();}
        catch(Exception ex){settings.PracticeArchiveScope=previous;_onlinePanel!.OnlinePracticeArchiveStatusText.Text="Could not pause: "+ex.Message;}
    }
    private async Task PublishPracticeArchiveAsync(OnlinePublishingSettings settings,OnlinePublisher publisher,string key)
    {
        _nextPracticeArchiveAttempt=DateTimeOffset.UtcNow.AddSeconds(10);
        try
        {
            var rows=await _practiceHistoryService.LoadArchiveCandidatesAsync(_settings.Track.Id,publisher.PracticeArchiveDelivered);
            if(rows.Count==0){_onlinePanel!.OnlinePracticeArchiveStatusText.Text=$"Practice history synchronized. {publisher.PracticeArchiveDelivered.Count} sessions acknowledged.";return;}
            var sessions=new List<System.Text.Json.JsonElement>();
            foreach(var row in rows)
            {
                var laps=await _practiceHistoryService.LoadLapsAsync(row.StintId);
                if(laps.Count!=row.LapCount)throw new InvalidOperationException("A saved practice session has incomplete lap history. It remains local.");
                sessions.Add(OnlineSnapshotBuilder.CapturePracticeArchive(row,laps));
            }
            if(!settings.Enabled || !PracticeArchiveEnabled(settings))return;
            SetOnlineTransferring(true);
            await publisher.UploadPracticeArchiveAsync(settings.BaseUrl,settings.OnlineTrackId,key,_settings.Track.Id,sessions,_onlineCancel.Token);
            _onlinePanel!.OnlinePracticeArchiveStatusText.Text=$"Practice history: {publisher.PracticeArchiveDelivered.Count} sessions acknowledged. Checking for more saved sessions.";
        }
        catch(Exception ex)
        {
            _nextPracticeArchiveAttempt=DateTimeOffset.UtcNow.AddSeconds(60);
            _onlinePanel!.OnlinePracticeArchiveStatusText.Text="Practice history pending. "+ex.Message+" Existing live publishing continues.";
        }
    }

    private static string UploadModeDescription(OnlinePublishingSettings settings)
    {
        var interval = $"{settings.UploadIntervalSeconds} {(settings.UploadIntervalSeconds == 1 ? "second" : "seconds")}";
        return settings.CompletedRacesOnly
            ? $"Race results after each race • practice every {interval}"
            : $"Race and practice uploading every {interval}";
    }
    private void ReadOnlineDataOptions(OnlinePublishingSettings target)
    {
        if (!long.TryParse(_onlinePanel!.OnlineLimitText.Text, out var limit) || limit < 0 || limit > 1_000_000_000)
            throw new ArgumentException("Enter a whole-number data limit in MB (0 = unlimited).");
        var completedOnly = _onlinePanel!.OnlineCompletedOnlyCheck.IsChecked == true;
        if (!int.TryParse(_onlinePanel!.OnlineIntervalText.Text, out var interval) ||
            interval is < OnlinePublishingSettings.MinimumUploadIntervalSeconds or > OnlinePublishingSettings.MaximumUploadIntervalSeconds)
            throw new ArgumentException("Enter a whole-number upload interval from 1 to 600 seconds.");
        target.UploadLimitMb = limit;
        target.CompletedRacesOnly = completedOnly;
        target.UploadIntervalSeconds = interval;
    }
    private void UpdateOnlineMeter()
    {
        if (_onlinePanel is null) return;
        try
        {
            var pub = GetPublisher(_settings.OnlinePublishing);
            var limit = _settings.OnlinePublishing.UploadLimitMb;
            var used = pub.UploadedBytes / 1_000_000d;
            _onlinePanel!.OnlineUsageText.Text = $"{used:N3} MB uploaded" + (limit > 0 ? $" / {limit:N0} MB limit" : " • Unlimited") +
                $" • Since {pub.MeterStartedAt.ToLocalTime():g}" + (WebsiteLimitBlocked() ? " • PAUSED AT LIMIT" : "");
            _onlinePanel!.OnlineUsageBar.Value = limit > 0 ? Math.Min(100, used / limit * 100) : 0;
        }
        catch (Exception ex) { _onlinePanel!.OnlineUsageText.Text = "Usage meter unavailable: " + ex.Message; }
    }
    private async void ApplyOnlineData_Click(object sender, RoutedEventArgs e)
    {
        if (_onlineBusy) { _onlinePanel!.OnlineStatusText.Text = "Please wait for the current upload before changing data settings."; return; }
        var settings = _settings.OnlinePublishing;
        var oldLimit = settings.UploadLimitMb; var oldMode = settings.CompletedRacesOnly; var oldInterval = settings.UploadIntervalSeconds;
        var applied = false;
        _onlineBusy = true;
        try
        {
            ReadOnlineDataOptions(settings);
            await CaptureAndSaveSettingsAsync(reportFailureToCaller: true);
            _onlineSchedule.Reset();
            _onlinePanel!.OnlineIntervalText.Text = settings.UploadIntervalSeconds.ToString();
            _onlinePanel!.OnlineStatusText.Text = "Saved. " + UploadModeDescription(settings);
            applied = true;
        }
        catch (Exception ex)
        {
            settings.UploadLimitMb = oldLimit; settings.CompletedRacesOnly = oldMode; settings.UploadIntervalSeconds = oldInterval;
            _onlinePanel!.OnlineStatusText.Text = "Data settings not saved: " + ex.Message;
        }
        finally { _onlineBusy = false; UpdateOnlineMeter(); UpdateWebsiteIndicator(); }
        if (applied && settings.Enabled) await PublishOnlineAsync(true);
    }
    private async void ResetOnlineMeter_Click(object sender, RoutedEventArgs e)
    {
        if (_onlineBusy) { _onlinePanel!.OnlineStatusText.Text = "Please wait for the current upload before resetting usage."; return; }
        _onlineBusy = true;
        try
        {
            await GetPublisher(_settings.OnlinePublishing).ResetMeterAsync();
            _onlineSchedule.Reset();
            _onlinePanel!.OnlineStatusText.Text = "Upload meter reset. Enabled publishing will resume within the selected limit.";
        }
        catch (Exception ex) { _onlinePanel!.OnlineStatusText.Text = "Could not reset usage: " + ex.Message; }
        finally { _onlineBusy = false; UpdateOnlineMeter(); UpdateWebsiteIndicator(); }
    }
    private void StopOnlinePublishing()
    {
        _onlineTimer.Stop(); _onlineCancel.Cancel();
        HeaderWebsiteIcon.BeginAnimation(UIElement.OpacityProperty, null);
        // Pending snapshots are already durable; current results are also retained by SQLite.
    }
}
