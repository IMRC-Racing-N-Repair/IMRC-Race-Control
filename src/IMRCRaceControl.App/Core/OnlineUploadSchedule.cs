using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;

/// <summary>The one-second dispatcher poll is independent of the chosen upload cadence.</summary>
public sealed class OnlineUploadSchedule
{
    private DateTimeOffset _nextSnapshot;
    private DateTimeOffset _retryAt;
    private int _failures;

    public bool SnapshotDue(OnlinePublishingSettings settings, DateTimeOffset now, bool force = false) =>
        settings.Enabled && (force || now >= _nextSnapshot && now >= _retryAt);

    public bool WaitingForRetry(DateTimeOffset now) => now < _retryAt;

    public void Started(OnlinePublishingSettings settings, DateTimeOffset now) =>
        _nextSnapshot = now.AddSeconds(settings.CompletedRacesOnly ? 1 : settings.UploadIntervalSeconds);

    // Results-only mode retains the one-second check for corrected results/Series.
    // A race completion can request the next available poll, without bypassing retry backoff.
    public void RequestSnapshot() => _nextSnapshot = DateTimeOffset.MinValue;

    public void Succeeded()
    {
        _failures = 0;
        _retryAt = DateTimeOffset.MinValue;
    }

    public int Failed(DateTimeOffset now)
    {
        _failures = Math.Min(6, _failures + 1);
        var seconds = Math.Min(120, 10 * (1 << (_failures - 1)));
        _retryAt = now.AddSeconds(seconds);
        RequestSnapshot();
        return seconds;
    }

    public void Reset()
    {
        Succeeded();
        RequestSnapshot();
    }
}

public enum WebsiteConnectionState { Disconnected, Paused, Ready, Uploading }

public static class WebsiteConnectionIndicator
{
    public static WebsiteConnectionState Resolve(bool configured, bool enabled, bool connected,
        bool uploading, bool limitBlocked, bool connecting = false, bool connectionFailed = false)
    {
        if (connecting) return WebsiteConnectionState.Uploading;
        if (!configured || connectionFailed && !enabled && !limitBlocked) return WebsiteConnectionState.Disconnected;
        if (!enabled || limitBlocked) return WebsiteConnectionState.Paused;
        if (uploading) return WebsiteConnectionState.Uploading;
        return connected ? WebsiteConnectionState.Ready : WebsiteConnectionState.Disconnected;
    }
}
