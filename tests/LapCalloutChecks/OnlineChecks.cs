using System.Net;
using System.Net.Http;
using System.Text.Json;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;
public static class OnlineChecks
{
    public static async Task<int> Run()
    {
        var checks=0;
        void Check(bool ok,string name){if(!ok)throw new Exception("Online: "+name);checks++;}
        var track=new TrackProfile{Name="Test track"};
        var registration=JsonDocument.Parse(OnlineTrackRegistration.BuildBody(track)).RootElement;
        Check(registration.GetProperty("uuid").GetString()==track.Id,"registration uses existing UUID");
        Check(registration.GetProperty("name").GetString()==track.Name,"registration name preserved");
        Check(!registration.TryGetProperty("key",out _),"registration body has no secret");
        var practiceNow=DateTimeOffset.UtcNow;
        var card=new PracticeCardViewModel{DriverId="PRIVATE_UUID",TransponderId="PRIVATE_TAG",DriverName="Yoshi",StartedAt=practiceNow.AddSeconds(-5),LastCrossingAt=practiceNow};
        card.AddLap(new PracticeLapEntry{LapNumber=1,LapTime=TimeSpan.FromSeconds(4.65),Timestamp=practiceNow});
        var practice=OnlineSnapshotBuilder.CapturePractice(true,[card],practiceNow);
        Check(practice.GetProperty("active").GetBoolean(),"practice active without event");
        Check(practice.GetProperty("drivers")[0].GetProperty("lapTimes")[0].GetDouble()==4.65,"practice lap precision");
        Check(!practice.GetRawText().Contains("PRIVATE_"),"practice excludes driver UUID and tag");
        Check(OnlineSnapshotBuilder.CapturePractice(false,[card],practiceNow).GetProperty("drivers").GetArrayLength()==0,"practice off clears roster");
        Check(OnlineSnapshotBuilder.CapturePractice(true,[card],practiceNow.AddMinutes(5)).GetProperty("drivers").GetArrayLength()==0,"idle practice drivers excluded");
        var racer=new DriverProfile{FirstName="Private first",LastName="Private last",Nickname="Public racer"};
        var cls=new ClassDefinition{Name="Stock"};
        var session=new EventSessionDefinition {ClassId=cls.Id,ClassName=cls.Name,RaceNumber=3,Round=2,Heat=1,
            Entrants=[new(){CarNumber=7,DriverId=racer.Id,DriverName=racer.FullName,Nickname=racer.Nickname,TransponderId="SECRET_TAG"}]};
        var ev=new RaceDayEventDefinition{Name="Night race",Track=track.Snapshot(),ClassIds=[cls.Id],Sessions=[session],DriverAssignments=[new(){ClassId=cls.Id,DriverId=racer.Id}]};
        var other=new RaceDayEventDefinition{Name="Other track"};
        var lap=new LapRecord(1,7,racer.DisplayName,1,TimeSpan.FromSeconds(4.65),TimeSpan.FromSeconds(4.65),DateTimeOffset.UtcNow,"decoder",false);
        var row=new StandingRow{Position=1,CarNumber=7,DriverName=racer.DisplayName,Laps=1,TotalTime="0:04.650",BestLap="4.650"};
        var capture=OnlineSnapshotBuilder.Capture(track,[ev,other],[],[racer],[cls],ev.Id,session.Id,RaceState.Running,[row],[lap],true);
        Check(capture.ExcludedEvents==1&&capture.Documents.Count==1,"only matching track exports");
        var payload=capture.Documents.Single().Value;
        var race=payload.GetProperty("races")[0];
        Check(race.GetProperty("status").GetString()=="live","live state");
        Check(race.GetProperty("round").GetString()=="Q2"&&race.GetProperty("number").GetInt32()==3,"stable round and race numbers");
        var result=race.GetProperty("drivers")[0];
        Check(result.GetProperty("carNumber").GetInt32()==7&&result.GetProperty("position").GetInt32()==1,"car and position distinct");
        Check(result.GetProperty("lapTimes")[0].GetDouble()==4.65,"decoder lap seconds preserved");
        Check(!payload.GetRawText().Contains(racer.Id)&&!payload.GetRawText().Contains("SECRET_TAG")&&!payload.GetRawText().Contains("Private first"),"private identity not exported");
        session.ProgressStatus=EventSessionStatus.Completed;session.Laps=[lap];session.Results=[row];ev.Status=EventStatus.Completed;
        var series=new RaceSeriesDefinition{Name="Cup",StartDate=ev.EventDate,EndDate=ev.EventDate,EventIds=[ev.Id],BestEventCount=1,
            Adjustments=[new(){ClassId=cls.Id,DriverId=racer.Id,Amount=-2,Note="Official adjustment"}]};
        var completed=OnlineSnapshotBuilder.Capture(track,[ev], [series],[racer],[cls],ev.Id,session.Id,RaceState.Ready,[],[],false);
        Check(completed.Documents.First().Value.GetProperty("races")[0].GetProperty("status").GetString()=="completed","completed results survive engine reset");
        var standings=SeriesScoring.Calculate(series,[ev],cls.Id,[racer]);
        var published=completed.Documents.Single(d=>d.Kind=="series").Value.GetProperty("drivers")[0];
        Check(published.GetProperty("total").GetDecimal()==standings[0].Points,"authoritative points incl adjustments");
        Check(published.GetProperty("position").GetInt32()==standings[0].Position,"authoritative tie-break position");
        Check(published.GetProperty("manualPoints").GetDecimal()==-2,"championship adjustment published");
        series.EventIds.Add(other.Id);
        Check(OnlineSnapshotBuilder.Capture(track,[ev,other],[series],[racer],[cls],ev.Id,null,RaceState.Ready,[],[],false).ExcludedSeries==1,"mixed-track Series blocked");
        foreach(var bad in new[]{"http://live.shopimrc.com","https://user:pass@example.com","https://example.com/api","https://example.com?key=bad"})
        {try{OnlinePublisher.ValidateBaseUrl(bad);throw new Exception("Accepted bad URL");}catch(ArgumentException){checks++;}}
        var folder=Path.Combine(Path.GetTempPath(),"imrc-outbox-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            var registering=new FakeHandler(HttpStatusCode.OK,"{\"apiVersion\":1,\"data\":{\"id\":\"registered-id\",\"name\":\"Server track\"}}");
            using(var pub=new OnlinePublisher(folder,"registration",registering))
            {
                var registrationResult=await pub.RegisterAsync("https://example.com",track,"registration-secret");
                Check(registrationResult.Id=="registered-id"&&registrationResult.Name=="Server track","registration response connects automatically");
                Check(registering.Url=="https://example.com/api/register","direct registration endpoint without browser login");
                Check(registering.Authorization=="Bearer registration-secret","private registration key header");
                Check(!registering.Url!.Contains("secret")&&!registering.Bodies[0].Contains("secret"),"key absent from URL and body");
                await pub.RegisterAsync("https://example.com",track,"registration-secret");
                Check(registering.Bodies[0]==registering.Bodies[1],"retry preserves registration identity");
            }
            foreach(var status in new[]{HttpStatusCode.Conflict,HttpStatusCode.Gone,HttpStatusCode.ServiceUnavailable})
            {
                using var rejected=new OnlinePublisher(folder,"registration",new FakeHandler(status));
                try{await rejected.RegisterAsync("https://example.com",track,"registration-secret");throw new Exception("Registration error accepted");}
                catch(HttpRequestException ex){Check(!ex.Message.Contains("registration-secret"),"registration failure hides key");}
            }
            var failed=new FakeHandler(HttpStatusCode.ServiceUnavailable);
            using(var pub=new OnlinePublisher(folder,"scope",failed))
            {
                await pub.CaptureAsync(capture with { Practice=practice },track.Id);
                Check(pub.PendingCount==2,"event and heartbeat queued");
                try{await pub.FlushAsync("https://example.com","online-id","test-key");throw new Exception("503 accepted");}catch(HttpRequestException){checks++;}
                Check(pub.PendingCount==2,"failed uploads retained");
                Check(failed.Authorization=="Bearer test-key","bearer authentication");
                Check(JsonDocument.Parse(failed.Bodies[0]).RootElement.GetProperty("practice").GetProperty("active").GetBoolean(),"practice survives snapshot serialization into durable outbox");
                Check(failed.Url!.EndsWith("/api/v1/tracks/online-id/snapshot"),"versioned endpoint");
            }
            var success=new FakeHandler(HttpStatusCode.OK);
            using(var pub=new OnlinePublisher(folder,"scope",success))
            {
                Check(pub.PendingCount==2,"outbox survives reopen");
                await pub.CaptureAsync(completed,track.Id);
                Check(pub.PendingCount==3,"latest event replaces pending live snapshot");
                await pub.FlushAsync("https://example.com","online-id","test-key");
                Check(pub.PendingCount==0,"successful uploads drain queue");
                var eventBody=success.Bodies.Select(x=>JsonDocument.Parse(x)).First(d=>d.RootElement.GetProperty("events").GetArrayLength()>0);
                Check(eventBody.RootElement.GetProperty("events")[0].GetProperty("status").GetString()=="completed","retry sends newest result");
                await pub.CaptureAsync(completed with {CurrentEventId=null,Documents=completed.Documents.Select(d=>d with {Heartbeat=false}).ToList()},track.Id);
                await pub.FlushAsync("https://example.com","online-id","test-key");
                await pub.CaptureAsync(completed with {CurrentEventId=null,Documents=completed.Documents.Select(d=>d with {Heartbeat=false}).ToList()},track.Id);
                Check(pub.PendingCount==1,"unchanged history not repeatedly uploaded");
                await pub.FlushAsync("https://example.com","online-id","test-key");
            }
            Check(Directory.GetFiles(folder,"*.json").All(f=>!File.ReadAllText(f).Contains("test-key")),"outbox contains no key");
            if(Environment.GetEnvironmentVariable("IMRC_CONTRACT_FIXTURE") is string fixture)
                await File.WriteAllTextAsync(fixture,JsonSerializer.Serialize(new{schemaVersion=1,localTrackId=track.Id,generatedAt=DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),currentEventId=ev.Id,events=completed.Documents.Where(d=>d.Kind=="event").Select(d=>d.Value),series=completed.Documents.Where(d=>d.Kind=="series").Select(d=>d.Value)},OnlineSnapshotBuilder.Json));
        }
        finally {Directory.Delete(folder,true);}
        return checks;
    }
    private sealed class FakeHandler(HttpStatusCode status,string response="{\"apiVersion\":1,\"data\":{\"accepted\":true}}"):HttpMessageHandler
    {
        public string? Authorization;public string? Url;public List<string> Bodies=new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Authorization=request.Headers.Authorization?.ToString();Url=request.RequestUri?.ToString();Bodies.Add(await request.Content!.ReadAsStringAsync(token));return new(status){Content=new StringContent(response)};}
    }
}
