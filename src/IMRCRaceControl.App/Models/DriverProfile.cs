namespace IMRCRaceControl.Models;

public sealed class DriverProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [System.Text.Json.Serialization.JsonIgnore]
    public string Uuid => IMRCRaceControl.Core.DriverIdentity.UuidFor(Id);

    /// <summary>
    /// Legacy field from pre-v0.8.1 builds.
    /// New code stores the driver's normal full name here only for backwards
    /// compatibility; the UI no longer exposes a separate Driver Name field.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;

    // Plain-language pronunciation used only for speech, never for display or identity.
    public string PhoneticName { get; set; } = string.Empty;
    public string SpokenName => string.IsNullOrWhiteSpace(PhoneticName)
        ? DisplayName : PhoneticName.Trim();

    public string FullName
    {
        get
        {
            var full = $"{FirstName} {LastName}".Trim();
            return string.IsNullOrWhiteSpace(full) ? Name.Trim() : full;
        }
    }

    /// <summary>
    /// Scoring/display rule:
    /// Nickname when present; otherwise first + last; otherwise first.
    /// </summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Nickname)
            ? Nickname.Trim()
            : FullName;

    public string DirectorySubtext
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Nickname) &&
                !string.IsNullOrWhiteSpace(FullName) &&
                !string.Equals(FullName, Nickname, StringComparison.OrdinalIgnoreCase))
            {
                return FullName;
            }

            return "Driver profile";
        }
    }

    public void SyncLegacyName() => Name = FullName;
}
