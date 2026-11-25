using System.Net;
using System.Text.Json;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;

internal static class PracticeArchiveChecks
{
    public static async Task<int> Run()
    {
        int checks=0;
        void Check(bool condition,string message)
        {if(!condition)throw new Exception("Practice archive: "+message);checks++;}
        var track=new TrackProfile {Id="venue-a",TimeZoneId="Eastern Standard Time"};
        var time=DateTimeOffset.Parse("2026-11-26T01:30:00Z");
        Check(OnlineSnapshotBuilder.PracticeDate(track,time)=="2026-11-25","late evening is grouped by venue local date");
        var folder=Path.Combine(Path.GetTempPath(),"imrc-practice-archive-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var service=new PracticeHistoryService(Path.Combine(folder,"practice.db"));
            PracticeCardViewModel Card(string id,string venue,DateTimeOffset start)
            {
                var c=new PracticeCardViewModel {StintId=id,DriverName="Public Racer",DriverId="private-uuid",TransponderId="001234",
                    TrackId=venue,PracticeDate=venue.Length>0?OnlineSnapshotBuilder.PracticeDate(track,start):"",
                    StartedAt=start,LastCrossingAt=start.AddSeconds(6)};
                c.AddLap(new PracticeLapEntry{LapNumber=1,LapTime=TimeSpan.FromSeconds(6),Timestamp=start.AddSeconds(6),Source="private-source"});
                return c;
            }
            var legacy=Card("old","",time);
            var later=Card("new",track.Id,time.AddMinutes(20));
            var other=Card("other","venue-b",time);
            foreach(var c in new[]{legacy,later,other})await service.SaveStintAsync(c,c.LastCrossingAt);
            var empty=new HashSet<string>();
            var rows=await service.LoadArchiveCandidatesAsync(track.Id,empty);
            Check(rows.Count==1 && rows[0].StintId=="new","unassigned legacy and other venues excluded before confirmation");
            Check(await service.AssignLegacyTrackAsync(track)==1,"only unassigned legacy session assigned");
            Check(await service.AssignLegacyTrackAsync(track)==0,"legacy assignment is one-time");
            rows=await service.LoadArchiveCandidatesAsync(track.Id,empty);
            Check(rows.Count==2 && rows.All(r=>r.PracticeDate=="2026-11-25"),"same-day stints have same date");
            Check((await service.LoadArchiveCandidatesAsync("venue-b",empty)).Single().StintId=="other","other track remains separate");
            var payload=new List<JsonElement>();
            foreach(var row in rows)payload.Add(OnlineSnapshotBuilder.CapturePracticeArchive(row,await service.LoadLapsAsync(row.StintId)));
            Check(payload.All(x=>x.GetProperty("transponderId").GetString()=="001234"),"TX preserves leading zeroes");
            Check(payload.All(x=>x.GetProperty("lapTimes")[0].GetDouble()==6),"all lap times retained");
            Check(payload.All(x=>!x.GetRawText().Contains("private-")),"no private driver UUID or decoder source");
            var handler=new Handler{Status=HttpStatusCode.NotFound};
            using(var pub=new OnlinePublisher(folder,"archive",handler))
            {
                try{await pub.UploadPracticeArchiveAsync("https://example.com","track","key",track.Id,payload);throw new Exception("Missing endpoint accepted");}catch(HttpRequestException){checks++;}
                Check(pub.PracticeArchiveDelivered.Count==0,"unavailable endpoint does not mark delivered");
                handler.Status=HttpStatusCode.OK;handler.WrongAck=true;
                try{await pub.UploadPracticeArchiveAsync("https://example.com","track","key",track.Id,payload);throw new Exception("Wrong ack accepted");}catch(HttpRequestException){checks++;}
                Check(pub.PracticeArchiveDelivered.Count==0,"wrong acknowledgement does not mark delivered");
                handler.WrongAck=false;
                Check(await pub.UploadPracticeArchiveAsync("https://example.com","track","key",track.Id,payload)==2,"server confirms both sessions");
                Check(handler.Url!.EndsWith("/api/v1/tracks/track/practice/archive"),"separate endpoint preserves existing live upload");
                Check(handler.Bodies.All(b=>!b.Contains("key")),"upload key not in payload");
                Check(pub.PracticeArchiveDelivered.Count==2,"stable stint IDs recorded");
            }
            using(var pub=new OnlinePublisher(folder,"archive",handler))
            {
                Check((await service.LoadArchiveCandidatesAsync(track.Id,pub.PracticeArchiveDelivered)).Count==0,"history not uploaded again after restart");
                var future=Card("future",track.Id,time.AddDays(1));await service.SaveStintAsync(future,future.LastCrossingAt);
                var next=(await service.LoadArchiveCandidatesAsync(track.Id,pub.PracticeArchiveDelivered)).Single();
                Check(next.StintId=="future" && next.PracticeDate=="2026-11-26","future practice automatically eligible under its own day");
                pub.UploadLimitBytes=pub.UploadedBytes;
                try{await pub.UploadPracticeArchiveAsync("https://example.com","track","key",track.Id,[OnlineSnapshotBuilder.CapturePracticeArchive(next,await service.LoadLapsAsync(next.StintId))]);throw new Exception("Archive bypassed meter");}catch(OnlineUploadLimitException){checks++;}
                Check(!pub.PracticeArchiveDelivered.Contains("future"),"budget-blocked session stays eligible");
            }
        }
        finally {Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(folder,true);}
        return checks;
    }
    private sealed class Handler:HttpMessageHandler
    {
        public HttpStatusCode Status=HttpStatusCode.OK;public bool WrongAck;public string? Url;public List<string> Bodies=new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            Url=request.RequestUri?.ToString();var body=await request.Content!.ReadAsStringAsync(cancellationToken);Bodies.Add(body);
            using var json=JsonDocument.Parse(body);
            var ids=json.RootElement.GetProperty("sessions").EnumerateArray().Select(x=>x.GetProperty("id").GetString()).ToArray();
            return new(Status){Content=new StringContent(JsonSerializer.Serialize(new {data=new {acceptedIds=WrongAck?new[]{"wrong"}:ids}}))};
        }
    }
}
