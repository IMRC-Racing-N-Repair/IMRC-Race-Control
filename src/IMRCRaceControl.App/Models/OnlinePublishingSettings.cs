namespace IMRCRaceControl.Models;
public sealed class OnlinePublishingSettings
{
    public string RegistrationTrackUuid { get; set; } = "";
    public bool CompletedRacesOnly { get; set; }
    public const int MinimumUploadIntervalSeconds = 1;
    public const int MaximumUploadIntervalSeconds = 600;
    private int _uploadIntervalSeconds = MinimumUploadIntervalSeconds;
    // Older profiles omit this property and retain the previous one-second cadence.
    public int UploadIntervalSeconds
    {
        get => _uploadIntervalSeconds;
        set => _uploadIntervalSeconds = Math.Clamp(value, MinimumUploadIntervalSeconds, MaximumUploadIntervalSeconds);
    }
    // Separate explicit opt-in, scoped to the connected track and server.
    public string PracticeArchiveScope { get; set; } = "";
    public long UploadLimitMb { get; set; }
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://live.shopimrc.com";
    public string OnlineTrackId { get; set; } = "";
    // Windows current-user DPAPI ciphertext, never the plaintext key.
    public string ProtectedUploadKey { get; set; } = "";
}
