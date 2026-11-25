# Practice archive 1.4 — deployment handoff

Status: Race Control client implemented; hosted IMRC Live backend has not been changed. Add the three routes in openapi.json to the existing site's backend, not a new service or a replacement for the existing snapshot endpoint.

## Storage and grouping
Use a D1 additive migration (do not drop/replace existing track, event, series or practice snapshot tables):

```sql
CREATE TABLE IF NOT EXISTS PracticeArchiveSessions (
  TrackId TEXT NOT NULL,
  StintId TEXT NOT NULL,
  PracticeDate TEXT NOT NULL,
  DriverName TEXT NOT NULL,
  TransponderId TEXT NOT NULL,
  StartedAt TEXT NOT NULL,
  EndedAt TEXT NOT NULL,
  LapCount INTEGER NOT NULL,
  BestLap REAL,
  AverageLap REAL,
  LapTimesJson TEXT NOT NULL,
  PRIMARY KEY (TrackId, StintId)
);
CREATE INDEX IF NOT EXISTS IX_PracticeArchive_Day
  ON PracticeArchiveSessions (TrackId, PracticeDate, StartedAt);
CREATE INDEX IF NOT EXISTS IX_PracticeArchive_TX
  ON PracticeArchiveSessions (TrackId, PracticeDate, TransponderId);
```

Each date is one practice event, independent of race events. Use the stored venue-local start date (YYYY-MM-DD), never group by UTC midnight. Use the track's registered time zone for validation. Show the label **MM/DD/YYYY - N Entries** where N is the number of distinct nonblank TX values, with each blank TX session counting separately. Session count is separate. All sessions remain individually accessible; do not merge or average away lap history. Group by exact TX string (including leading zeros). For blank TX, use the stint ID, never join different anonymous racers. Use session display names; do not upload private driver identifiers or contact details.

## API write
POST /api/v1/tracks/{trackId}/practice/archive: authenticate using the existing track upload-key verification and local-track ownership validation. Reject unregistered/deleted tracks. Enforce HTTPS, same-origin writes, 2 MB limit, 50 sessions per batch. Validate entire batch before a D1 atomic batch of INSERT ... ON CONFLICT(TrackId,StintId) DO UPDATE, using parameter binding. Require unique IDs, parseable date/timestamps, nonblank stint ID, name and TX bounded strings, finite positive lap times and lapCount equal to length. Do not update currentEventId, heartbeat, active practice snapshot or existing results. Acknowledge only after commit: {"apiVersion":1,"data":{"acceptedIds":["stint-id",...]}}. A repeat must not increase the entry or session count. No shared archive timestamp rejection: historical dates are expected.

## API reads
GET /api/v1/tracks/{trackId}/practice/days: paginate dates newest first; use distinct TX count + anonymous stint count and total session count per date. Return items and nextOffset.
GET /api/v1/tracks/{trackId}/practice/days/{date}?q=&offset=0&limit=50: date-scoped search, case-insensitive driver name or exact/partial TX matching; use bound query parameters and escaped LIKE characters. Include all matching sessions across pages, newest first; return day, items and nextOffset. When a name matches one session for a TX on that day, include all sessions for that TX. Return public reads with existing CORS and cache policy. Never expose upload keys or internal IDs.

Frontend: practice date selector uses the day title; rows are grouped by TX, with total laps, number of sessions, fastest lap and lap-weighted average. Clicking opens lap-time graph with independent session toggles and expandable newest-first lap list. Fetch all result pages before computing daily statistics. If archive routes are absent, show current practice only and a clear unavailable message, not fabricated archived results.

## Deployment acceptance
Test using an isolated test track: replay same batch, duplicate IDs, wrong key, wrong track, invalid lap, UTC midnight boundary, multiple sessions on one date, another date, leading-zero TX, offline replay and pagination. Confirm existing live race and practice snapshots, registration, results, series and SSE continue working unchanged. Keep prior public site/data intact. Only then deploy to live.shopimrc.com.
