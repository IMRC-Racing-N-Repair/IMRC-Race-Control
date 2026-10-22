/** Public read client. Never put a Race Control upload key in a spectator app. */
export type Page<T> = {items:T[];nextOffset:number|null};
export type Track = {id:string;name:string;location:string;surface:string;updatedAt:string;currentEventId:string|null;lastReceivedAt:string|null};
export type Driver = {name:string;position:number;carNumber?:number;laps:number;time:string;best?:string;lastLap?:string;gap?:string;finished?:boolean;lapTimes?:number[]};
export type Race = {id:string;number:number;round:string;classId?:string;className:string;heat:string;status:'planned'|'live'|'completed'|'skipped';drivers:Driver[]};
export type EventSummary = {id:string;name:string;date:string;status:'upcoming'|'active'|'completed';raceCount:number;updatedAt:string};
export type Event = Omit<EventSummary,'raceCount'> & {races:Race[]};
export type Practice = {id:"current";active:boolean;updatedAt:string;stale:boolean;drivers:{id:string;name:string;laps:number;lastLap:string;best:string;average:string;lastCrossingAt:string;lapTimes:number[];stats:{label:string;value:string}[]}[]};
export type Live = {track:Track;mode:"race"|"practice";practice:Practice|null;event:Event|null;races:Race[];generatedAt:string|null;receivedAt:string|null;stale:boolean;pollAfterSeconds:number};
export type SeriesSummary = {id:string;name:string;className:string;classId?:string;bestOf:number;updatedAt:string};
export type Series = SeriesSummary & {weeks:string[];eventIds?:string[];authoritative?:boolean;drivers:{name:string;position:number;total:number;points:(number|null)[];counted:number[];manualPoints?:number;notes?:string[]}[]};
export class ImrcApiError extends Error {constructor(public status:number,message:string){super(message)}}
export class ImrcLiveClient {
  private cache=new Map<string,{etag:string;data:unknown}>();
  constructor(private base='https://live.shopimrc.com/api/v1'){}
  private async get<T>(path:string,signal?:AbortSignal):Promise<T>{
    const previous=this.cache.get(path);
    const response=await fetch(this.base.replace(/\/$/,'')+path,{signal,headers:previous?{'If-None-Match':previous.etag}:{}});
    if(response.status===304&&previous)return previous.data as T;
    const body=await response.json() as {data:T;error?:{message?:string}};
    if(!response.ok)throw new ImrcApiError(response.status,body.error?.message||'IMRC Live request failed');
    const etag=response.headers.get('etag');if(etag){if(this.cache.size>100)this.cache.clear();this.cache.set(path,{etag,data:body.data});}
    return body.data as T;
  }
  tracks(offset=0,limit=50,signal?:AbortSignal){return this.get<Page<Track>>(`/tracks?offset=${offset}&limit=${limit}`,signal)}
  track(id:string,signal?:AbortSignal){return this.get<Track>(`/tracks/${encodeURIComponent(id)}`,signal)}
  live(id:string,signal?:AbortSignal){return this.get<Live>(`/tracks/${encodeURIComponent(id)}/live`,signal)}
  practice(id:string,signal?:AbortSignal){return this.get<{track:Track;practice:Practice|null;pollAfterSeconds:number}>(`/tracks/${encodeURIComponent(id)}/practice`,signal)}
  events(track:string,offset=0,limit=50,signal?:AbortSignal){return this.get<Page<EventSummary>>(`/tracks/${encodeURIComponent(track)}/events?offset=${offset}&limit=${limit}`,signal)}
  event(track:string,id:string,filters:{round?:string;classId?:string;status?:Race['status']}={},signal?:AbortSignal){const q=new URLSearchParams(filters);return this.get<Event>(`/tracks/${encodeURIComponent(track)}/events/${encodeURIComponent(id)}?${q}`,signal)}
  race(track:string,event:string,id:string,signal?:AbortSignal){return this.get<Race>(`/tracks/${encodeURIComponent(track)}/events/${encodeURIComponent(event)}/races/${encodeURIComponent(id)}`,signal)}
  series(track:string,offset=0,limit=50,signal?:AbortSignal){return this.get<Page<SeriesSummary>>(`/tracks/${encodeURIComponent(track)}/series?offset=${offset}&limit=${limit}`,signal)}
  standings(track:string,id:string,signal?:AbortSignal){return this.get<Series>(`/tracks/${encodeURIComponent(track)}/series/${encodeURIComponent(id)}`,signal)}
}

/** Browser EventSource helper. Native apps can use an SSE library against the same URL.
 * Each `live` event contains {apiVersion:1,data:Live}. Reconnects provide a fresh full snapshot.
 * Call the returned function when leaving the screen. No upload key is required. */
export function subscribeLive(trackId:string,onLive:(data:Live)=>void,onError?:()=>void,base='https://live.shopimrc.com/api/v1'){
 const source=new EventSource(base.replace(/\/$/,'')+'/tracks/'+encodeURIComponent(trackId)+'/stream');
 source.addEventListener('live',event=>{try{onLive(JSON.parse((event as MessageEvent).data).data)}catch{onError?.()}});
 source.onerror=()=>onError?.();source.addEventListener('unavailable',()=>onError?.());return()=>source.close();
}
