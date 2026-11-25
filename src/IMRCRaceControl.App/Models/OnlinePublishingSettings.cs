namespace IMRCRaceControl.Models;
public sealed class OnlinePublishingSettings
{
    public string RegistrationTrackUuid { get; set; } = "";
    public bool CompletedRacesOnly { get; set; }
    // Separate explicit opt-in, scoped to the connected track and server.
    public string PracticeArchiveScope { get; set; } = "";
    public long UploadLimitMb { get; set; }
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://live.shopimrc.com";
    public string OnlineTrackId { get; set; } = "";
    // Windows current-user DPAPI ciphertext, never the plaintext key.
    public string ProtectedUploadKey { get; set; } = "";
}
