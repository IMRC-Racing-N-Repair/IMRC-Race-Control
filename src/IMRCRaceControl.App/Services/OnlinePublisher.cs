using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IMRCRaceControl.Core;
namespace IMRCRaceControl.Services;

public sealed class PendingOnlineUpload
{
    public string Hash { get; set; } = "";
    public string GeneratedAt { get; set; } = "";
    public string Body { get; set; } = "";
}
public sealed class OnlineOutboxState
{
    public string LastGeneratedAt { get; set; } = "";
    public Dictionary<string, string> Delivered { get; set; } = new();
    public Dictionary<string, PendingOnlineUpload> Pending { get; set; } = new();
}
// One instance is scoped to server + online track + local track. No secret enters this file.
// Caller serializes Capture/Flush operations. Latest snapshot replaces pending data per record.
public sealed class OnlinePublisher : IDisposable
{
    private readonly string _statePath;
    private readonly HttpClient _http;
    private OnlineOutboxState _state = new();
    public int PendingCount => _state.Pending.Count;
    public static Uri ValidateBaseUrl(string address)
    {
        if (!Uri.TryCreate(address.Trim().TrimEnd('/'), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            throw new ArgumentException("Enter an HTTPS site address without a path, query or password.");
        return uri;
    }
    public OnlinePublisher(string directory, string scope, HttpMessageHandler? handler = null)
    {
        Directory.CreateDirectory(directory);
        _statePath = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope))) + ".json");
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
        if (File.Exists(_statePath))
        {
            try { _state = JsonSerializer.Deserialize<OnlineOutboxState>(File.ReadAllText(_statePath)) ?? new(); }
            catch (JsonException) { File.Move(_statePath, _statePath + ".invalid-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true); }
        }
    }
    private async Task SaveAsync()
    {
        var temp = _statePath + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(_state));
        File.Move(temp, _statePath, true);
    }
    public async Task CaptureAsync(OnlineCapture capture, string localTrackId, bool force = false)
    {
        var now = DateTimeOffset.UtcNow;
        if (DateTimeOffset.TryParse(_state.LastGeneratedAt, out var previous) && now <= previous) now = previous.AddMilliseconds(1);
        var generatedAt = now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        _state.LastGeneratedAt = generatedAt;
        var docs = capture.Documents.Select(d => (Key: d.Kind + ":" + d.Id, Doc: (OnlineDocument?)d)).ToList();
        docs.Add(("heartbeat", null));
        foreach (var (id, doc) in docs)
        {
            var fingerprint = (doc?.Value.GetRawText() ?? "") + "|" + capture.CurrentEventId;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
            if (!force && doc is not null && !doc.Heartbeat && _state.Delivered.GetValueOrDefault(id) == hash && !_state.Pending.ContainsKey(id)) continue;
            var body = JsonSerializer.Serialize(new { schemaVersion = 1, localTrackId, generatedAt, currentEventId = capture.CurrentEventId,
                practice = capture.Practice,
                events = doc?.Kind == "event" ? new[] { doc.Value } : Array.Empty<JsonElement>(),
                series = doc?.Kind == "series" ? new[] { doc.Value } : Array.Empty<JsonElement>() }, OnlineSnapshotBuilder.Json);
            if (Encoding.UTF8.GetByteCount(body) > 2000000) throw new InvalidOperationException($"{id} exceeds the 2 MB upload limit. Other records remain queued; contact IMRC for a larger-event upload format.");
            _state.Pending[id] = new PendingOnlineUpload { Hash = hash, GeneratedAt = generatedAt, Body = body };
        }
        await SaveAsync();
    }
    public async Task<(string Id, string Name)> RegisterAsync(string baseUrl, IMRCRaceControl.Models.TrackProfile track, string key, CancellationToken cancel = default)
    {
        var origin = ValidateBaseUrl(baseUrl);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, "api/register"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(OnlineTrackRegistration.BuildBody(track), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancel);
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new HttpRequestException("This UUID is already registered with another upload key. Contact shopimrc@gmail.com for recovery; its existing data is unchanged.");
        if (response.StatusCode == HttpStatusCode.Gone)
            throw new HttpRequestException("IMRC deleted this track. Contact shopimrc@gmail.com to restore registration.");
        var text = await response.Content.ReadAsStringAsync(cancel);
        EnsureSuccess(response, text);
        using var json = JsonDocument.Parse(text);
        var data = json.RootElement.GetProperty("data");
        var id = data.GetProperty("id").GetString();
        if (string.IsNullOrWhiteSpace(id)) throw new HttpRequestException("Registration response was incomplete. Click Register Track Online to retry safely.");
        return (id, data.GetProperty("name").GetString() ?? track.Name);
    }
    public async Task<string> TestAsync(string baseUrl, string trackId, string key, string localTrackId, CancellationToken cancel = default)
    {
        using var request = Request(baseUrl, trackId, "connection", key, JsonSerializer.Serialize(new { localTrackId }));
        using var response = await _http.SendAsync(request, cancel);
        var text = await response.Content.ReadAsStringAsync(cancel);
        EnsureSuccess(response, text);
        using var json = JsonDocument.Parse(text);
        return json.RootElement.GetProperty("data").GetProperty("track").GetProperty("name").GetString() ?? trackId;
    }
    public async Task<int> FlushAsync(string baseUrl, string trackId, string key, CancellationToken cancel = default, Func<bool>? shouldContinue = null)
    {
        var sent = 0;
        foreach (var item in _state.Pending.OrderBy(x => x.Value.GeneratedAt, StringComparer.Ordinal).ThenBy(x => x.Key).ToList())
        {
            if (shouldContinue is not null && !shouldContinue()) break;
            using var request = Request(baseUrl, trackId, "snapshot", key, item.Value.Body);
            using var response = await _http.SendAsync(request, cancel);
            var text = await response.Content.ReadAsStringAsync(cancel);
            EnsureSuccess(response, text);
            _state.Delivered[item.Key] = item.Value.Hash;
            _state.Pending.Remove(item.Key);
            await SaveAsync();
            sent++;
        }
        return sent;
    }
    private static HttpRequestMessage Request(string baseUrl, string trackId, string operation, string key, string body)
    {
        var origin = ValidateBaseUrl(baseUrl);
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, "api/v1/tracks/" + Uri.EscapeDataString(trackId) + "/" + operation));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }
    private static void EnsureSuccess(HttpResponseMessage response, string text)
    {
        if (response.IsSuccessStatusCode) return;
        var message = response.StatusCode switch {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Upload key rejected. Check the track ID and key in Track Info.",
            HttpStatusCode.Conflict => "Track identity or snapshot conflict. Use the correct track and keep only one race PC publishing to it.",
            HttpStatusCode.RequestEntityTooLarge => "Upload exceeds the server's 2 MB limit.",
            HttpStatusCode.TooManyRequests => "Hosting rate limit reached. Updates remain queued for retry.",
            _ => $"Upload failed (HTTP {(int)response.StatusCode}). Updates remain queued." };
        // Never copy a response body or credential into logs/UI.
        throw new HttpRequestException(message, null, response.StatusCode);
    }
    public void Dispose() => _http.Dispose();
}
