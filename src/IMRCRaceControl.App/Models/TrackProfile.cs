namespace IMRCRaceControl.Models;

// Stable identity is independent of the display name and future online account.
public sealed class TrackProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = "United States";
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public string TrackType { get; set; } = string.Empty;
    public string Surface { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string PublicEmail { get; set; } = string.Empty;
    public string PublicPhone { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public TrackProfile Snapshot() => (TrackProfile)MemberwiseClone();
}
