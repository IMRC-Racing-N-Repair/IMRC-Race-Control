using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
namespace IMRCRaceControl.Services;
/// <summary>Read-only LAN scoreboard. No files or control commands.</summary>
public sealed class LocalScoreboardServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<Task<string>> _snapshot;
    public string Url { get; }
    public LocalScoreboardServer(Func<Task<string>> snapshot)
    {
        _snapshot=snapshot;
        _listener=new TcpListener(IPAddress.Any,8787);
        _listener.Start(); Url="http://127.0.0.1:8787/";
        _=Run();
    }
    public static string[] NetworkUrls() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
        .Select(a => $"http://{a.Address}:8787/").Distinct().ToArray();

    private async Task Run()
    {
        try { while(!_stop.IsCancellationRequested) {
            using var client=await _listener.AcceptTcpClientAsync(_stop.Token);
            try {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); timeout.CancelAfter(3000);
                var stream=client.GetStream(); var bytes=new byte[4096]; var count=await stream.ReadAsync(bytes,timeout.Token);
                var request=Encoding.ASCII.GetString(bytes,0,count).Split('\r','\n')[0];
                var json=request.StartsWith("GET /state ",StringComparison.Ordinal);
                var page=request.StartsWith("GET / ",StringComparison.Ordinal);
                var body=json?await _snapshot():page?Html:"Not found";
                var payload=Encoding.UTF8.GetBytes(body);
                var header=Encoding.ASCII.GetBytes($"HTTP/1.1 {(json||page?"200 OK":"404 Not Found")}\r\nContent-Type: {(json?"application/json":"text/html")}; charset=utf-8\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header,timeout.Token); await stream.WriteAsync(payload,timeout.Token);
            } catch(Exception ex) when(ex is not OutOfMemoryException) { if(!_stop.IsCancellationRequested) DecoderDiagnostics.Write("LOCAL BOARD",ex.Message); }
        }} catch(OperationCanceledException) {} catch(SocketException) when(_stop.IsCancellationRequested) {}
    }
    public void Dispose() { _stop.Cancel(); _listener.Stop(); }
    private const string Html="""
<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"><title>IMRC Spectator Dashboard</title><style>
*{box-sizing:border-box}body{margin:8px;background:var(--DashboardBackgroundBrush,#10151d);color:var(--DashboardTextBrush,#fff);font:17px system-ui}header{text-align:center;padding:10px;background:var(--DashboardPanelBrush);border-radius:8px}h1{font-size:30px;margin:0}#subtitle{font-size:19px}#clock{font-size:56px;font-weight:bold}#status{font-size:12px;opacity:.75}#next{color:white;background:var(--AccentDarkBrush,#7e2e35)!important;overflow:hidden;padding:10px;margin:7px 0;background:var(--DashboardPanelBrush);white-space:nowrap}#next span{display:inline-block}#next.scroll span{animation:scroll 22s linear infinite}@keyframes scroll{from{transform:translateX(100vw)}to{transform:translateX(-100%)}}main{display:grid;grid-template-columns:minmax(0,1fr);gap:8px}main.overall{grid-template-columns:minmax(0,3fr) minmax(290px,1fr)}.box{overflow:auto;background:var(--DashboardPanelBrush);padding:5px;border-radius:8px}table{border-collapse:collapse;width:100%;white-space:nowrap}td,th{padding:8px;text-align:center;border-bottom:1px solid #526178}th{font-size:12px}td{height:56px;font-weight:600;background:var(--DashboardRowBrush)}tr:nth-child(even) td{background:var(--DashboardAlternateRowBrush)}.name{display:block;min-width:160px;padding:8px 4px;border:2px solid transparent;border-radius:6px}.unchecked .name{border-color:var(--DashboardCheckInGlowBrush)}.crossing .name{border-color:var(--DriverCrossingBrush)}.underline .crossing .name{border-width:0 0 3px;border-radius:0}.minimum .name,.underline .minimum .name{border-color:var(--DashboardMinimumLapGlowBrush);border-width:2px;border-radius:6px}.fastest td{background:var(--DashboardFastestLapHighlightBrush);color:var(--DashboardFastestLapHighlightTextBrush)}.finished-row td{background:var(--FinishedDriverBrush);color:var(--FinishedDriverContrastBrush)}.finished-text td{color:var(--FinishedDriverBrush)}td.best{background:var(--DashboardNewBestLapHighlightBrush)!important;color:var(--DashboardNewBestLapHighlightTextBrush)!important}.up{color:var(--DashboardPositionUpBrush)}.down{color:var(--DashboardPositionDownBrush)}.same{color:var(--DashboardPositionSameBrush)}h2{font-size:16px;margin:8px}.current{font-weight:900}#practice{display:grid;grid-template-columns:repeat(auto-fit,minmax(250px,1fr));gap:12px}.card{background:var(--DashboardPanelBrush);padding:16px;border-radius:8px}@media(max-width:950px){main.overall{grid-template-columns:1fr}}[hidden]{display:none!important}

html{height:100%;overflow:hidden}body{height:calc(100dvh - 16px);display:flex;flex-direction:column;overflow:hidden}#screen{flex:1;min-height:0;display:flex;flex-direction:column;overflow:hidden}#tools input[type=range]{width:150px;accent-color:#d62835}header,#next,#tools{flex:none}main{flex:1;min-height:0;grid-template-rows:minmax(0,1fr)}.box{min-height:0;min-width:0;overscroll-behavior:contain}#overall{display:flex;flex-direction:column;overflow:hidden}#overall h2,#overall p{flex:none}#projectionScroll{min-height:0;flex:1;overflow:auto;overscroll-behavior:contain}#board > section > table{zoom:var(--screenZoom,1)}#practice .card{zoom:var(--screenZoom,1)}body.zoom-header header{zoom:var(--screenZoom,1)}#projectionScroll table{zoom:calc(var(--overallZoom,1) * var(--screenZoom,1));font-size:13px}#projectionScroll td{height:40px;padding:6px}#projectionScroll thead{position:sticky;top:0;background:var(--DashboardPanelBrush);z-index:1}#practice{flex:1;min-height:0;overflow:auto}#tools{display:flex;justify-content:flex-end;gap:5px;align-items:center;padding:4px;font-size:13px;flex-wrap:wrap}#tools button{background:var(--DashboardPanelBrush,#263141);color:inherit;border:1px solid #526178;border-radius:4px;padding:4px 9px;cursor:pointer}@media(max-width:950px){main.overall{grid-template-rows:minmax(0,1fr) minmax(0,1fr)}}
</style></head><body><div id="screen"><header><h1 id="title">IMRC Race Control</h1><div id="subtitle"></div><div id="clock"></div><div id="status">Connecting…</div></header><div id="next"><span></span></div><main id="board"><section class="box"><table><thead id="heads"></thead><tbody id="rows"></tbody></table></section><aside class="box" id="overall"><h2>ESTIMATED OVERALL POSITION</h2><p id="info"></p><div id="projectionScroll"><table><thead><tr><th>POS</th><th>DRIVER</th><th>PACE / FINISH</th><th>MOVE</th></tr></thead><tbody id="projections"></tbody></table></div></aside></main><section id="practice" hidden></section></div><nav id="tools" aria-label="Display zoom"><span>Drivers</span><label><input id="zoomHeader" type="checkbox" onchange="setHeaderZoom(this.checked)"> Include header</label><button onclick="adjustZoom('screen',-5)" aria-label="Zoom screen out">−</button><input id="screenSlider" type="range" min="50" max="150" step="5" value="100" aria-label="Screen zoom" oninput="setZoom('screen',this.value)"><span id="screenPercent"></span><button onclick="adjustZoom('screen',5)" aria-label="Zoom screen in">+</button><span>Overall</span><button onclick="adjustZoom('overall',-5)" aria-label="Zoom overall positions out">−</button><input id="overallSlider" type="range" min="50" max="150" step="5" value="100" aria-label="Overall positions zoom" oninput="setZoom('overall',this.value)"><span id="overallPercent"></span><button onclick="adjustZoom('overall',5)" aria-label="Zoom overall positions in">+</button><button onclick="resetZoom()">Reset</button></nav><script>
const el=id=>document.getElementById(id),text=(id,v)=>el(id).textContent=v??'';
function setHeaderZoom(value){document.body.classList.toggle('zoom-header',value);el('zoomHeader').checked=value;try{localStorage.setItem('imrc-zoom-header',String(value))}catch{}}
try{setHeaderZoom(localStorage.getItem('imrc-zoom-header')==='true')}catch{}
const zoom={screen:100,overall:100};
for(const k of Object.keys(zoom)){try{const n=Number(localStorage.getItem('imrc-'+k+'-zoom'));if(Number.isFinite(n)&&n>=50&&n<=150)zoom[k]=n}catch{}}
function applyZoom(){for(const k of Object.keys(zoom)){document.documentElement.style.setProperty('--'+k+'Zoom',zoom[k]/100);text(k+'Percent',zoom[k]+'%');el(k+'Slider').value=zoom[k];try{localStorage.setItem('imrc-'+k+'-zoom',zoom[k])}catch{}}}
function setZoom(k,n){zoom[k]=Math.max(50,Math.min(150,Math.round(Number(n)/5)*5));applyZoom()}
function adjustZoom(k,n){zoom[k]=Math.max(50,Math.min(150,zoom[k]+n));applyZoom()}
function resetZoom(){zoom.screen=100;zoom.overall=100;applyZoom()}applyZoom();

const fields={'POS':'Position','#':'CarNumber','DRIVER':'DriverName','LAPS':'Laps','LAST LAP':'LastLap','BEST LAP':'BestLapDisplay','CLOCK':'Clock','GAP LEADER':'GapToLeader','GAP BEHIND':'GapBehind','TOP 2':'Top2','TOP 3':'Top3','TOP 5':'Top5','AVG':'AverageLap','AVG 5':'Average5','AVG 10':'Average10','AVG 15':'Average15','CONSIST':'Consistency','PACE':'Pace','TOTAL':'TotalTime','STATUS':'Status'};
function cell(tr,value,tag='td'){const td=document.createElement(tag);td.textContent=value??'—';tr.append(td);return td}
function render(d){for(const [k,v] of Object.entries(d.colors))document.documentElement.style.setProperty('--'+k,v);el('title').hidden=el('subtitle').hidden=d.showHeader===false;el('clock').hidden=d.showClock===false;el('next').hidden=d.showNext===false;text('title',d.title);text('subtitle',d.subtitle);text('clock',d.clock);text('status',d.practice?'PRACTICE':d.state);const n=el('next');n.hidden=!d.next||d.showNext===false;if(d.next){n.firstElementChild.textContent=d.next.Heading+' • '+d.next.Names;n.classList.add('scroll')}el('board').hidden=d.practice;el('practice').hidden=!d.practice;el('board').className=(d.showOverall?'overall ':'')+(d.crossingStyle==='Underline'?'underline':'');el('overall').hidden=!d.showOverall;text('info',d.projectionInfo);
const h=document.createElement('tr');for(const c of d.columns)cell(h,c,'th');el('heads').replaceChildren(h);
const rows=document.createDocumentFragment();for(const r of d.rows){const tr=document.createElement('tr');tr.className=[!r.IsCheckedIn?'unchecked':'',r.IsMinimumLapViolationGlow?'minimum':'',r.IsLastPassing?'crossing':'',r.IsFastestLap?'fastest':'',r.IsFinished?(d.finishedStyle==='Row'?'finished-row':d.finishedStyle==='Text'?'finished-text':''):''].join(' ');for(const c of d.columns){const td=cell(tr,r[fields[c]]);if(c==='POS'&&r.IsFinished){const span=document.createElement('span');span.textContent=r.Position;span.style.cssText='display:block;border:2px solid #FFD54F;border-radius:6px;padding:8px 4px';td.replaceChildren(span)}if(c==='DRIVER'){const span=document.createElement('span');span.className='name';span.textContent=(r.IsCheckedIn?'✓ ':'')+r.DriverName;td.replaceChildren(span)}if(c==='BEST LAP'&&r.IsNewPersonalBest)td.className='best'}rows.append(tr)}el('rows').replaceChildren(rows);
const projection=document.createDocumentFragment();for(const r of d.projections){const tr=document.createElement('tr');if(r.IsCurrentHeat)tr.className='current';cell(tr,r.ProjectedPositionDisplay);cell(tr,r.DriverName);cell(tr,r.PaceFinish);const move=cell(tr,r.MovementDisplay);move.className=(r.MovementState||'').toLowerCase();projection.append(tr)}el('projections').replaceChildren(projection);
const cards=document.createDocumentFragment();for(const r of d.practices){const card=document.createElement('article');card.className='card';const h=document.createElement('h2');h.textContent=r.name;card.append(h);for(const t of [r.laps+' laps','Last '+r.last+' • Best '+r.best,'Average '+r.average,...r.stats.map(s=>s.label+': '+s.value)]){const p=document.createElement('p');p.textContent=t;card.append(p)}cards.append(card)}el('practice').replaceChildren(cards)}
async function update(){try{const r=await fetch('/state',{cache:'no-store',signal:AbortSignal.timeout(3000)});if(!r.ok)throw Error();render(await r.json())}catch{text('status','DISCONNECTED — last received timing')}setTimeout(update,100)}update();
</script></body></html>
""";
}
