using System.Windows;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private void LoadTrackInfo()
    {
        TrackRecordsPanel.Configure(() => (_settings.Track, Events.ToList(), Classes.ToList()));
        var track = _settings.Track;
        TrackIdText.Text = track.Id;
        TrackTimeZoneCombo.ItemsSource = TimeZoneInfo.GetSystemTimeZones();
        TrackTimeZoneCombo.SelectedValue = track.TimeZoneId;
        TrackNameText.Text = track.Name;
        TrackAddressText.Text = track.Address;
        TrackCityText.Text = track.City;
        TrackRegionText.Text = track.Region;
        TrackPostalCodeText.Text = track.PostalCode;
        TrackCountryText.Text = track.Country;
        TrackTrackTypeText.Text = track.TrackType;
        TrackSurfaceText.Text = track.Surface;
        TrackWebsiteText.Text = track.Website;
        TrackPublicEmailText.Text = track.PublicEmail;
        TrackPublicPhoneText.Text = track.PublicPhone;
        TrackNotesText.Text = track.Notes;
    }

    private async void SaveTrackInfo_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TrackNameText.Text))
        {
            MessageBox.Show(this, "Enter a track name.", "Track information");
            TrackNameText.Focus(); return;
        }
        var website = TrackWebsiteText.Text.Trim();
        if (website.Length > 0 && (!Uri.TryCreate(website, UriKind.Absolute, out var url) ||
            (url.Scheme != "https" && url.Scheme != "http")))
        {
            MessageBox.Show(this, "Enter a full website address beginning with https:// or http://.", "Track information");
            return;
        }
        if (TrackTimeZoneCombo.SelectedValue is not string zone)
        {
            MessageBox.Show(this, "Select the track time zone.", "Track information"); return;
        }
        var previous = _settings.Track;
        var track = previous.Snapshot();
        track.Name = TrackNameText.Text.Trim();
        track.Address = TrackAddressText.Text.Trim();
        track.City = TrackCityText.Text.Trim();
        track.Region = TrackRegionText.Text.Trim();
        track.PostalCode = TrackPostalCodeText.Text.Trim();
        track.Country = TrackCountryText.Text.Trim();
        track.TrackType = TrackTrackTypeText.Text.Trim();
        track.Surface = TrackSurfaceText.Text.Trim();
        track.Website = TrackWebsiteText.Text.Trim();
        track.PublicEmail = TrackPublicEmailText.Text.Trim();
        track.PublicPhone = TrackPublicPhoneText.Text.Trim();
        track.Notes = TrackNotesText.Text.Trim();
        track.TimeZoneId = zone;
        track.UpdatedAtUtc = DateTimeOffset.UtcNow;
        _settings.Track = track;
        try
        {
            await CaptureAndSaveSettingsAsync(reportFailureToCaller: true);
            TrackRecordsPanel.Refresh();
            TrackSaveStatusText.Text = $"Saved {track.Name}. New events will use this track.";
        }
        catch (Exception ex)
        {
            _settings.Track = previous;
            MessageBox.Show(this, $"Track information could not be saved: {ex.Message}", "Save failed");
        }
    }

    private async void AssignTrackToEvent_Click(object sender, RoutedEventArgs e)
    {
        var ev = _activeEvent;
        if (ev is null || string.IsNullOrWhiteSpace(_settings.Track.Name))
        {
            MessageBox.Show(this, "Save track information and select an event in Event Setup first.", "Track information"); return;
        }
        if (MessageBox.Show(this, $"Assign {_settings.Track.Name} to '{ev.Name}'? This replaces this event's stored track information.",
            "Assign event track", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var previous = ev.Track;
        ev.Track = _settings.Track.Snapshot();
        try
        {
            await CaptureAndSaveSettingsAsync(reportFailureToCaller: true);
            TrackRecordsPanel.Refresh();
            TrackSaveStatusText.Text = $"Assigned {ev.Track.Name} to {ev.Name}.";
        }
        catch (Exception ex)
        {
            ev.Track = previous;
            MessageBox.Show(this, $"Event track could not be saved: {ex.Message}", "Save failed");
        }
    }
}
