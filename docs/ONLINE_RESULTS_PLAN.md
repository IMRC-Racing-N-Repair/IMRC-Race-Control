# IMRC Live connection — v0.15.2

Implemented: anonymous one-click desktop UUID registration with private-key retry; administrator-only profile edits, key replacement and whole-track deletion with UUID blocking; opt-in HTTPS publishing, track-specific encrypted upload credentials, durable retry outbox, stable event/race IDs, public v1 read API and authoritative Series standings.

See the main README for pairing instructions, limits and verification. Full API specification and a TypeScript client for IMRC Setup Manager are in docs/IMRC_LIVE_API. The live reference is https://live.shopimrc.com/api-docs.

Next-stage features, not implemented: video, account-linked driver identities, remote setup writeback, multi-track Series publishing, owner-managed pairing reset and automatic cloud backups. Local scoring and SQLite remain authoritative.

## Race settings and Pace (v0.17.19)

The existing v1 snapshot POST and public event/live/race responses accept these
additive, optional fields. No change to schemaVersion, endpoint, authentication,
track identity or database is needed.

- Race: `raceFormat` is `"timed"` or `"laps"`. Timed races include
  `durationSeconds` (a positive number); lap races include `targetLaps` (a positive integer).
  Only the applicable length is transmitted. Active and just-finished races use the
  actual engine settings, including in-race changes. Completed races use the
  persisted engine settings; for older sessions without a saved override, the
  existing class qualifying/main duration is used when available, just as stored
  standings are rebuilt. Planned races use the saved session format, session
  duration override or class duration, and session lap target (with the same
  class-target fallback used at race start). Both classes in a combined on-track
  run use the same active engine settings. Unknown or invalid settings are omitted,
  not replaced by a fixed default.
- Driver: `pace` is the desktop projected laps/finish-time string (e.g.
  `45/4:58.381`) for live and completed rows. The average is calculated from actual
  scored full-lap durations at decoder precision, including a single completed
  lap; a tone-start baseline crossing is not a lap. Timed pace projects
  floor(duration / average), at least the completed lap count, multiplied by the
  average. Lap-target pace projects targetLaps × average. Times are formatted by
  RaceEngine.FormatElapsed (minutes:seconds.milliseconds). This remains a
  projection for completed races, not the actual result time. Combined races use
  mapped runtime car numbers for live rows/laps and original car numbers for
  saved results. The value is `"—"` when a projection is unavailable (no lap,
  inconsistent/missing lap records, or invalid configuration). Planned/skipped
  rows omit pace.
- Spectator clients should display the supplied string as-is; older records may
  omit any of these fields. Do not infer a five-minute duration or substitute
  laps completed for the uploaded pace.

See `IMRC_LIVE_API/openapi.json` and `IMRC_LIVE_API/imrc-live-client.ts`. Regression
checks assert the serialized snapshot and outgoing HTTP request body using a
mock server; they do not send requests to registered tracks.

## Website Connection settings (v0.17.20)

Open Settings → Website Connection, or click the globe next to the decoder
status in the top-right toolbar. Existing track registration, encrypted upload
key, upload limit and practice archive opt-in are retained. Apply upload
settings on that page; use TEST CONNECTION / SAVE to change connection details
or enable publishing. PAUSE PUBLISHING stops automatic requests, without
removing public results.

Select **Uploading every N seconds** (1–600 whole seconds; defaults to the
previous one-second cadence) to send live race/practice snapshots on that
interval. Or select **Upload results after each race** to send completed race
results and updated Series standings, without ongoing live race/practice
timing; completion requests the next one-second scheduler poll and unchanged
results are deduplicated. A single metered connection verification after a
restart may be necessary when no new result needs uploading. PUBLISH NOW
requests a sync immediately, bypassing cadence but not pause/data limits.
Practice archive batches, if separately enabled, remain independent of live
cadence and respect the same data meter. Failed snapshots retry with bounded
backoff; scoring never waits for the network.

The globe is green when publishing is enabled and the last connection
succeeded, pulses green during a transfer/connection attempt, orange when
paused or at the upload budget, and red when unconfigured or disconnected.
Click to see the connection details and most recent error. Between requests
the status is the last known connection state. Long intervals can mean that
IMRC Live marks timing stale between snapshots; choose a short interval for
near-real-time spectator timing.
