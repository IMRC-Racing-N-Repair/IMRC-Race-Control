using System.Net;
using System.Text;
using System.Text.Json;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;

internal static class OnlineDataChecks
{
    public static async Task<int> Run()
    {
        int checks=0;
        void Check(bool value,string message) { if(!value)throw new Exception("Online data: "+message);checks++; }
        var track=new TrackProfile { Name="Budget track" };
        var ev=new RaceDayEventDefinition { Track=track,Sessions=[new() { Id="done",ProgressStatus=EventSessionStatus.Completed },new() { Id="live",ProgressStatus=EventSessionStatus.Running },new() { Id="planned" }] };
        OnlineCapture Capture(bool mode)=>OnlineSnapshotBuilder.Capture(track,[ev],[],[],[],ev.Id,"live",RaceState.Running,[],[],true,mode) with { Practice=OnlineSnapshotBuilder.CapturePractice(false,[],DateTimeOffset.UtcNow) };
        var completed=Capture(true);
        Check(completed.Documents.Single().Value.GetProperty("races").GetArrayLength()==1,"only completed races in payload");
        Check(completed.Documents.Single().Value.GetProperty("races")[0].GetProperty("id").GetString()=="done","correct saved race selected");
        Check(!completed.Documents.Single().Heartbeat,"active event stops periodic uploads");
        var folder=Path.Combine(Path.GetTempPath(),"imrc-budget-"+Guid.NewGuid().ToString("N"));
        try
        {
            var handler=new Handler();long used;
            using(var pub=new OnlinePublisher(folder,"policy",handler))
            {
                await pub.CaptureAsync(Capture(false),track.Id);
                await pub.CaptureAsync(completed,track.Id,completedOnly:true);
                await pub.FlushAsync("https://example.com","track","key");
                Check(handler.Bodies.All(b=>!b.Contains("\"status\":\"live\"")),"queued live snapshot purged on mode switch");
                Check(handler.Bodies.All(b=>!b.Contains("\"id\":\"planned\"")),"planned races not uploaded");
                used=handler.Bodies.Sum(b=>(long)Encoding.UTF8.GetByteCount(b));
                Check(pub.UploadedBytes==used,"meter counts exact UTF8 payload bytes");
                await pub.CaptureAsync(completed,track.Id,completedOnly:true);
                Check(pub.PendingCount==0,"unchanged results-only capture has no heartbeat traffic");
                await pub.FlushAsync("https://example.com","track","key");
                Check(pub.UploadedBytes==used,"idle mode sends zero data");
            }
            using(var pub=new OnlinePublisher(folder,"policy",handler=new Handler()))
            {
                Check(pub.UploadedBytes==used,"meter survives restart");
                await pub.CaptureAsync(completed,track.Id,force:true,completedOnly:true);
                pub.UploadLimitBytes=used+1;
                try { await pub.FlushAsync("https://example.com","track","key");throw new Exception("Cap bypassed"); } catch(OnlineUploadLimitException) { checks++; }
                Check(handler.Bodies.Count==0 && pub.UploadedBytes==used && pub.PendingCount>0,"over-budget request never sent and remains queued");
                Check(pub.LimitBlocked,"meter flags limit pause");
                await pub.ResetMeterAsync();
                Check(pub.UploadedBytes==0 && !pub.LimitBlocked,"reset clears counter and pause");
                pub.UploadLimitBytes=used;
                await pub.FlushAsync("https://example.com","track","key");
                Check(pub.UploadedBytes==used && pub.PendingCount==0,"exact remaining budget allowed after reset");
                await pub.CaptureAsync(completed,track.Id,completedOnly:true);
                Check(pub.PendingCount==0,"reset does not lose delivered state");
                await pub.CaptureAsync(Capture(false),track.Id);
                pub.UploadLimitBytes=0;await pub.FlushAsync("https://example.com","track","key");
                Check(handler.Bodies.Any(b=>b.Contains("\"status\":\"live\"")),"switching back resumes live uploads");
            }
            using(var pub=new OnlinePublisher(folder,"failures",handler=new Handler { Status=HttpStatusCode.ServiceUnavailable }))
            {
                await pub.CaptureAsync(completed,track.Id,completedOnly:true);
                try { await pub.FlushAsync("https://example.com","track","key"); } catch(HttpRequestException) { }
                var first=pub.UploadedBytes;
                Check(first>0,"failed request counted");
                try { await pub.FlushAsync("https://example.com","track","key"); } catch(HttpRequestException) { }
                Check(pub.UploadedBytes==first*2,"retry counts again");
                pub.UploadLimitBytes=pub.UploadedBytes;
                try { await pub.TestAsync("https://example.com","track","key",track.Id);throw new Exception("Test bypassed cap"); } catch(OnlineUploadLimitException) { checks++; }
                try { await pub.RegisterAsync("https://example.com",track,"key");throw new Exception("Register bypassed cap"); } catch(OnlineUploadLimitException) { checks++; }
            }
            var settings=JsonSerializer.Deserialize<OnlinePublishingSettings>(JsonSerializer.Serialize(new OnlinePublishingSettings { CompletedRacesOnly=true,UploadLimitMb=25 }))!;
            Check(settings.CompletedRacesOnly && settings.UploadLimitMb==25,"mode and limit persisted");
            Check(!new OnlinePublishingSettings().CompletedRacesOnly && new OnlinePublishingSettings().UploadLimitMb==0,"existing installations retain unlimited live default");
        }
        finally { Directory.Delete(folder,true); }
        return checks;
    }
    private sealed class Handler : HttpMessageHandler
    {
        public HttpStatusCode Status=HttpStatusCode.OK;
        public List<string> Bodies=new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancel));
            return new(Status) { Content=new StringContent("{\"data\":{\"accepted\":true}}") };
        }
    }
}
