# Changelog

Keep all release history in this file. Add each new version at the top; do not create version-specific changelog files.

## 0.17.20
- Move IMRC Live Website Connection into Settings and add a clickable globe in the upper-right toolbar: green when connected and enabled, pulsing green during transfers, orange when paused or at the data limit, red when disconnected.
- Set an upload interval from 1–600 seconds (one second remains the default) and independently check Upload results after each race. With that box checked, live practice still uploads at the selected interval, while races upload upon completion. Preserve existing credentials, data limit and practice archive opt-in.
- Add five customizable F1–F5 spoken announcements in Settings → Keybinds, with individual Test buttons. Messages use the selected Windows voice, announcement speed and volume, persist across restarts, and work in the focused main window during practice or racing; blank messages are disabled.

## 0.17.19
- Publish effective timed/lap race settings and per-driver projected Pace to IMRC Live for live and completed races, using the desktop calculation (including one scored lap), session overrides and combined-race car mapping. Existing API clients and registrations remain compatible.
- Add opt-in one-time practice history backfill and automatic future completed practice uploads, using track-local dates, public driver display names and TX numbers, stable stint IDs, and durable acknowledgement tracking. Existing live timing remains independent if the archive endpoint is unavailable.

## 0.17.18
- Restore sequential race numbers after explicit lineup moves and allow cross-class heat interleaving. Preserve manual order when selecting rosters and rebuilding the saved queue; keep completed races locked and unaffected rounds unchanged.
- Separate lineup mouse clicks from native grid selection and race movement. Preserve the visible order and race numbers during roster selection, use one theme-colored selected row without a second hover/focus highlight, and require a MOVE-handle drag or explicit reorder action.
- Fix lineup selection to highlight only one race during normal clicking, preserve race order when opening rosters, and require dragging the MOVE handle or using explicit reorder buttons. Shift-click combining remains available.
- Race Order Assistant now offers one qualifying or mains round at a time, with a round picker and independent previews; all other rounds remain fixed.
- Remember the Event screen registration/rounds divider position across restarts using a saved proportional height; retain resizing limits and restore it when browsing active events again.
- Validate suggested race orders against the same queue-rebuild logic used when saving, so only reproducible improvements can be applied.
- Enlarge practice LAST timing and live lap history text; compact the secondary statistics without changing automatic card sizing or lap order.
- Add a Race Order Assistant with Least Changes, Fewest Conflicts and Maximum Rest suggestions, side-by-side order comparison, and explicit Apply to Preview. Suggestions are bounded, preserve locked/loaded and combined races, same-class sequence and bump-up dependencies, and never change driver rosters or save automatically.
- Show an offline What's New dialog after updating, with cumulative changes since the last recorded version and one-time acknowledgement. Existing installations without a recorded version see recent release notes on the first update.
- Redesign the consecutive-race warning with theme-aware text and surfaces, affected-driver and conflict counts, a scrollable driver/first race/next race table, and clear Change Lineup and Continue Anyway actions.
- Add Overall Results to qualifying seeding, using each driver's best completed run across earlier rounds. New Q1 defaults to Random; Q2 and later and new mains default to Overall Results. Keep other sorting choices, balanced heats, slower heats first, and saved lineups unchanged.
- Live practice laps now appear newest-first, with compact timing and lap rows. The chosen 4/5/6 columns remain fixed; cards use full height when one row is needed, shorten automatically when more drivers join, and expand again when drivers time out. Shared dashboard and spectator card template; chronological scoring and saved history remain unchanged.
- Add practice layout and newest-first regression checks.

## 0.17.17
- Show a floating race or driver preview that follows the mouse in Rounds / Mains Lineups. Valid race destinations show a theme-colored insertion line above or below the target row; valid driver destinations highlight the entire receiving heat. Invalid targets and cancelled drags do not change the lineup. Keep completed races locked and edits pending until Save Changes.
- Match floating drag previews and drop indicators to the active theme (panel, accent, border and text colors); add regression checks for theme resource updates, overlay positioning, before/after race insertion and destination highlighting.

## 0.17.16
- Refine the narrow Live Round Position panel: fit complete driver names without ellipses, center names geometrically, enlarge POS and movement numbers, give movement badges their own inset space, retain yellow live-racer text and red/green movement highlights; add narrow-panel visual regression coverage.
- Improve Live Round Position readability: vertically center driver names, enlarge and pad POS numbers, inset movement badges, and use theme-aware yellow text for current-heat racers (even when an earlier qualifying result remains their best); preserve readable green/red movement indicators.
- Make the NEXT UP banner responsive on the race dashboard and spectator display: size the heading to its content, scroll from just after the heading to the right edge, and repeat short lineups across wide screens without large blank areas.
- Fix the qualifying dashboard XAML resource scope: share the centered cell style between standings and projection grids, and require a dedicated real-dashboard startup regression before installer packaging.
- Stabilize event schedule column fitting against layout feedback and isolate dashboard-based schedule and spectator UI previews from operator database saves and startup; retain combined-race sorting and selection checks.
- Isolate automated WPF preview startup from normal application startup and run each UI validation in a separate workflow step with live diagnostic checkpoints; preserve required checks and draft-only packaging.
- Carry forward all 0.17.15 race-flow changes into this cumulative unpublished draft.
- Fix the unattended Windows breakout-style regression by testing scoped WPF resources without waiting for dispatcher idle; verify persistent pending breakout and crossing highlight restoration.
- Remove the forced three-minute preview shutdown; keep required UI and installer validation, explicit exit-code failure reporting, and restore the unsaved heat drag regression check.
- Preserve the 0.17.14 main branch; isolate draft build concurrency by branch.
- Center the live qualifying POSITION, PACE / FINISH, and pace-gap figures; move the incremental +/− position change to a small colored superscript at the upper-right of each driver's name.
- Replace the MOVE column with PACE GAP: time behind the current class leader for the same projected lap count, lap deficit where counts differ, and a negative time for the leader when faster than the established previous qualifying TQ (dash otherwise). Preserve the existing qualifying-rank and race scoring calculations.
- Persistently highlight the race-wide fastest valid BEST LAP cell for the entire active session using the configured fastest-lap color; transfer the highlight only when a faster valid lap replaces it, independent of personal-best notifications.
- Restore drag-and-drop for newly generated heats before the first Save; add a visible MOVE grip and keep HEAT UP / HEAT DOWN available. Renumber the editable preview immediately without changing saved/completed races.
- Display simultaneous breakout notices one per line beneath the race results, with a bounded scroll area for larger fields.
- Keep every pending breakout visible until that driver records a valid lap; support undo and clear on new/restarted races.
- Make rounded practice cards follow the selected Light, Dark, or Blue theme while retaining a subtle gradient/radiant overlay, theme accent stripe, and soft shadow.
- Mains seeding now offers **OVERALL ROUND RESULTS** (default for new mains): rank racers by their best single completed qualifying run across every round, rather than the final round only. The prior round-placement ranking remains available as a separate choice.
- Tone-start races keep the master clock running from the start tone but ignore each racer's first crossing for scored laps and breakout detection. That crossing becomes the baseline for Lap 1; later complete laps use crossing-to-crossing time. Practice and alternative start styles retain their behavior.
- Before saving a generated or edited lineup, detect drivers appearing in consecutive races and show the driver and both race names/numbers with CHANGE LINEUP or CONTINUE ANYWAY. CHANGE returns to editing; every subsequent save checks again. Combined class sessions in the same on-track race are excluded.
- Add regression checks for ignored tone-start first crossings and protected master-clock timing.

- Generate evenly balanced qualifying heats instead of filling the first heat before the next (12 racers with max 9 become 6/6).
- Default ranked and previous-round qualifying heats to slower-seeded racers first; mains retain lower-main-first generation.
- Renumber uncompleted qualifying heats in their displayed order without changing completed heats or their saved heat numbers.
- Add configurable race START (S) and FALSE START (F) keyboard shortcuts under Settings > Keybinds; ignore typing in input fields and repeat keypresses.
- Add small orange driver-name breakout counters, a bottom breakout lap notice, and persistent orange driver highlighting until a valid lap is accepted; honor the existing breakout highlight color.
- Add a configurable Practice idle multiplier (default 20× average lap, range 2–60) to Settings > Practice.
- Highlight the fastest valid lap across the whole live session in green while preserving the configurable Fastest Lap color.
- Rename GAP BEHIND to GAP AHEAD and calculate the positive interval to the next car ahead; show a dash for the leader.
- Show best-of-qualifiers projected overall position in the race POS display while preserving the official per-heat scoring position; the POS cell shows position only.
- Announce lap times by truncating to tenths, never rounding (4.399 speaks as 4.3); update speech regression expectations.
- Allow uncompleted A/B/C mains to be moved independently across main legs; preserve manual race order and protect completed race data.
- Prompt operators to check/uncheck and confirm bump-up drivers after a lower-main series ends; no automatic additions without confirmation.
- Requires Windows UI, race-director workflow, and timing-decoder regression testing prior to publication. This build is intended as an unpublished testing draft.

## 0.17.15
- Center the live qualifying POSITION, PACE / FINISH, and pace-gap figures; move the incremental +/− position change to a small colored superscript at the upper-right of each driver's name.
- Replace the MOVE column with PACE GAP: time behind the current class leader for the same projected lap count, lap deficit where counts differ, and a negative time for the leader when faster than the established previous qualifying TQ (dash otherwise). Preserve the existing qualifying-rank and race scoring calculations.
- Persistently highlight the race-wide fastest valid BEST LAP cell for the entire active session using the configured fastest-lap color; transfer the highlight only when a faster valid lap replaces it, independent of personal-best notifications.
- Restore drag-and-drop for newly generated heats before the first Save; add a visible MOVE grip and keep HEAT UP / HEAT DOWN available. Renumber the editable preview immediately without changing saved/completed races.
- Display simultaneous breakout notices one per line beneath the race results, with a bounded scroll area for larger fields.
- Show multiple simultaneous breakout notices as compact orange chips at the bottom of the race screen. Keep all consecutive breakouts (even from the same driver) visible until that driver records a valid lap; support undo and clear on new/restarted races.
- Make rounded practice cards follow the selected Light, Dark, or Blue theme while retaining a subtle gradient/radiant overlay, theme accent stripe, and soft shadow.
- Mains seeding now offers **OVERALL ROUND RESULTS** (default for new mains): rank racers by their best single completed qualifying run across every round, rather than the final round only. The prior round-placement ranking remains available as a separate choice.
- Tone-start races keep the master clock running from the start tone but ignore each racer's first crossing for scored laps and breakout detection. That crossing becomes the baseline for Lap 1; later complete laps use crossing-to-crossing time. Practice and alternative start styles retain their behavior.
- Before saving a generated or edited lineup, detect drivers appearing in consecutive races and show the driver and both race names/numbers with CHANGE LINEUP or CONTINUE ANYWAY. CHANGE returns to editing; every subsequent save checks again. Combined class sessions in the same on-track race are excluded.
- Add regression checks for ignored tone-start first crossings and protected master-clock timing.

- Generate evenly balanced qualifying heats instead of filling the first heat before the next (12 racers with max 9 become 6/6).
- Default ranked and previous-round qualifying heats to slower-seeded racers first; mains retain lower-main-first generation.
- Renumber uncompleted qualifying heats in their displayed order without changing completed heats or their saved heat numbers.
- Add configurable race START (S) and FALSE START (F) keyboard shortcuts under Settings > Keybinds; ignore typing in input fields and repeat keypresses.
- Add small orange driver-name breakout counters, a bottom breakout lap notice, and persistent orange driver highlighting until a valid lap is accepted; honor the existing breakout highlight color.
- Add a configurable Practice idle multiplier (default 20× average lap, range 2–60) to Settings > Practice.
- Highlight the fastest valid lap across the whole live session in green while preserving the configurable Fastest Lap color.
- Rename GAP BEHIND to GAP AHEAD and calculate the positive interval to the next car ahead; show a dash for the leader.
- Show best-of-qualifiers projected overall position in the race POS display while preserving the official per-heat scoring position; include estimated A/B/C Main cutoffs.
- Announce lap times by truncating to tenths, never rounding (4.399 speaks as 4.3); update speech regression expectations.
- Allow uncompleted A/B/C mains to be moved independently across main legs; preserve manual race order and protect completed race data.
- Prompt operators to check/uncheck and confirm bump-up drivers after a lower-main series ends; no automatic additions without confirmation.
- Requires Windows UI, race-director workflow, and timing-decoder regression testing prior to publication. This build is intended as an unpublished testing draft.

## 0.17.14
- Refresh live practice driver panels with rounded 12px corners, a subtle dark-blue gradient, a slim cyan header accent, and a soft shadow while preserving large readable lap timing and flat statistics.
- Shorten the practice Top 2 Consecutive display label to Top 2 Cons. and align stat values on a common horizontal row without changing saved stat identifiers.
- Simplify live practice driver cards by removing the transponder subtitle and lap source text. Show signed three-decimal differences from each preceding lap, green for faster, red for slower, neutral for equal or first laps.
- Restore the saved physical decoder connection on application startup; retain the saved COM port instead of connecting to another detected port. Add a clickable global decoder icon with red disconnected, green connected and orange error states.
- Expand live practice tiles to use available rows and columns based on active driver count; enlarge timing text and simplify nested statistics panels.
- Keep Trackmate's known-working 9600-baud setting as the default while allowing operators to select and save other serial baud rates. The selected rate is passed directly to the serial port and must match the decoder's configured serial output.
- Add a visible decoder warning after repeated invalid Trackmate passing frames, directing operators to check their serial baud setting and decoder logs. Do not mistake incoming bytes or decoder-side detection for valid scored lap passes.
- Reset Trackmate invalid-frame warnings after a valid passing or connection reset. Leave other decoder protocols and their saved baud rates unchanged.
- Hardware validation remains required for non-9600 Trackmate rates. No claim of automatic baud negotiation or support for an unverified decoder output rate.

## 0.17.13
- Show a compact "PB: laps/time" line directly beneath the driver name only when the driver's current projected race pace is better than their previous overall race-result PB for the same driver/class/format/distance.
- On a confirmed completed result that beats the prior PB, replace that line with the newly achieved laps/time in larger, bold, bright green text. An initial completed result also establishes a first PB.
- Preserve the existing lap-time highlight as a distinct feature. PB predictions are not official records before race finish; ties, slower pace and incomplete fixed-lap distances do not count.
- Add a separately configurable "New completed-race PB text" color under Settings > Appearance > Dashboard Colors, default #39FF65; live preview, validation and save persist it without forcing the other dashboard theme colors to custom.
- Update the in-memory historical PB index as completed races are saved so subsequent heats compare against freshly achieved records without waiting for restart.
- Add automated timed/fixed-lap race PB presentation checks. Draft only pending Windows build and live race-day testing.

## 0.17.12
- Speed up Track Info records: one pass through completed qualifying/main laps for all classes instead of repeatedly traversing all imported laps for every class.
- Cache the ranked track records when changing class, track and print count; explicitly pressing REFRESH or editing track assignment invalidates the cache.
- Keep only the top three on-screen, with the chosen top-N available for print/PDF.
- Clarify the existing PB behavior: the live lap highlight indicates a better lap within the current heat; saved historical personal bests are race-result based, and do not yet trigger a separate live career-PB notification.
- Preserve 0.17.11 dashboard layout upgrade choice; draft-only pending Windows verification and real large-import speed testing.

## 0.17.11
- Remove STATUS and ACTION columns from the live race dashboard; retain manual race check-in/crossing keyboard shortcuts.
- Move PACE directly next to LAPS in the default race grid layout.
- Default visible columns to all remaining race information except TOTAL, CONSIST, AVG 5, AVG 10, AVG 15, and TOP 5. The excluded columns remain available in dashboard customization.
- On first launch after updating an existing installation, display a one-time choice: Use New Race Data Layout or Keep Current Layout. Retain all existing visibility/order preferences unless the operator explicitly opts into the new compact defaults; fresh installs use the new defaults without an upgrade prompt.
- Keep 0.17.10 instant registration counts and responsive event class changes. Draft-only pending Windows verification and real race-day testing.

## 0.17.10
- Update the selected driver's Classes & Transponders count immediately after a checkbox change without rebuilding the driver list; singular and plural labels update correctly.
- Show a live (#) count in the upper-right corner of each class tile for the number of distinct racers assigned to that class in the current event.
- Update only the affected driver label and class counter using in-memory assignments, preserving the non-blocking click behavior from 0.17.9.
- Keep ordered, targeted SQLite registration saving in the background. Draft-only, pending Windows verification and live testing with imported data.

## 0.17.9
- Fix the actual Event > Drivers > Classes & Transponders grid: do not disable the entire control for each checkbox or transponder edit, and do not rebuild every driver/class tile after saving.
- Make changes to the selected driver/class immediately in memory, while an ordered background worker persists only the event's ClassIds, DriverAssignments and affected transponders in pending heats.
- Persist targeted changes with SQLite JSON1 updates to canonical AppState and queryable projections; leave historical results and laps unchanged.
- Remove full event registration/round recalculation and full-state snapshot save from the individual tile click handler.
- Add database regression checks verifying targeted registration changes preserve unrelated race history.
- This patch is draft pending user testing with a large imported database and final Windows verification.

## 0.17.8
- Eliminate full-history serialization when adding or removing an event class with no scheduled heats to delete.
- Persist only the current event ClassIds using a lightweight atomic JSON1 SQLite update, plus the matching Events and EventClasses projections; never touch lap/result tables for those changes.
- Queue small class-mutation operations in the same ordered database worker as full saves to preserve edit ordering and allow instant consecutive clicks.
- Keep full transactional reconciliation when removing a class also removes planned races. Preserve completed race history and protective prompts.
- Add isolated regression checks for membership persistence, unrelated events and preservation of historic laps.
- The rest of the application still has some full-state save paths; broader latency profiling is needed for any remaining delay. This draft needs real-database testing before publication.

## 0.17.7
- Fix event class checkboxes freezing the UI: update only the clicked class membership and event-class summary in memory without reconstructing the entire checkbox list, planned sessions, or live race dashboard per click.
- Recalculate the race queue only when removing a class actually deletes uncompleted heats; protect completed race results with existing confirmation rules.
- Defer event-class driver roster reconstruction until the operator stops clicking; don't refresh unrelated driver-registration views for each selection.
- Preserve 0.17.6 ordered background database writes and 0.17.5 LAPS display. Noncritical full-state snapshot capture now runs at application-idle priority after 1.5 seconds of inactivity instead of interrupting successive user clicks. Critical race and shutdown saves remain immediate. Snapshot serialization can still affect the UI with very large histories and requires future incremental refactoring.
- Draft only pending Windows tests and real large-database responsiveness verification.

## 0.17.6
- Separate race UI and database persistence into independent execution workloads: the WPF dispatcher handles interface and scoring presentation, while an ordered thread-pool worker handles SQLite writes, settings backup, JSON decoding and historical PB calculations.
- Freeze race state before handing it to the background worker; preserve strict save ordering and finalize queued database writes before application shutdown.
- Add isolated regression checks for ordered successive saves and settings recovery.
- On computers with at least two logical CPU cores Windows can run the UI and background writer concurrently; do not pin threads or require a specific hardware core count. No guarantee of fixed 2-core utilization.
- Retain the 0.17.5 pre-start LAPS counter, class-click debounce, background data persistence and prior import/race functionality.
- UI snapshot capture is still serialized on the dispatcher and remains a future optimization target for exceptionally large histories.
- Draft-only; verify with real hardware and an imported racing database before publication.

## 0.17.5
- Display "Laps 0/50" (or the selected target) on the race dashboard as soon as a LAPS class or loaded session is selected, before START; retain live lap progress on all spectator displays.
- Make routine UI edits nonblocking: coalesce successive changes with an approximately 700 ms idle debounce, so clicking another class does not wait for SQLite I/O.
- Force immediate durable persistence for critical operations and final application shutdown; serialize an isolated snapshot before the asynchronous database write so subsequent edits cannot corrupt an in-flight save.
- Continue 0.17.4's targeted class and per-event SQLite updates, virtualized driver lists, Trackmate 9600 baud, LiveTime/RCSP imports, and race-result personal bests.
- Draft-only build; validate rapid-change recovery and performance with large imported databases before publishing.

## 0.17.4
- Replace expensive full-history database rewrites with transactionally scoped updates: only modified classes and their main rules, changed race events and their result/lap rows, and modified driver/assignment/series groups are rewritten; unchanged historical events are retained.
- Reduce class-click delays by skipping redundant event-registration rebuilds, enabling virtualized/recycled driver roster rows, and batching large roster list updates into a single UI notification. Default to background SQLite serialization and saves so large histories do not block WPF while persisting changes.
- Add isolated SQLite regression checks verifying class changes do not delete historical lap records and editing one race event preserves unrelated data.
- Keep lap-based races running until the leader first reaches the configured target; trailing racers finish on their next valid crossing. Add a dedicated 100-lap regression scenario.
- Package the common LiveTime backup unlock key through the private LIVETIME_IMPORT_PASSWORD Actions secret; fail the draft installer build if the secret is absent rather than silently shipping a build without automatic unlocking.
- Retain automatic Race Control / LiveTime / RC Scoring Pro source recognition, Trackmate 9600 defaults and timed/fixed-lap result-based Personal Bests.
- Still requires Windows testing and safe data/backups review before publishing.

## 0.17.3
- Show the top three Track Records per class on the Track Info screen, without changing stored records or the adjustable print/PDF record count.
- Display a centered IMRC Race Control logo loading screen immediately at launch, with progress shown through database initialization and main dashboard setup.
- Simplify Saved Race Data to one format-detecting IMPORT RACE DATA action; remove the redundant RCSP folder inspection button.
- Preserve 9600 baud as the default for Trackmate RC, the LiveTime and RC Scoring Pro import paths, and timed/fixed-lap race-result Personal Bests from 0.17.2.
- Automatic import detection now attempts read-only legacy/encoded and encrypted LiveTime backup readers before showing an unknown-format message; retain the existing manual unlock fallback. A private Actions secret can bundle the shared LiveTime unlock credential at installer build time without committing it to source.
- Coalesce rapid imported-event selections before recalculating duplicate review and planned database merges. Existing imports are written into Race Control's own SQLite database; large full-snapshot saves can still take longer and require separate performance profiling.
- Keep the release as a draft for validation, with existing user data unchanged.

## 0.17.2
- Keep Trackmate RC decoder on 9600 baud by default, with 8-N-1 and no handshake. One-time migrate previously saved 115200 default selections to 9600, while preserving future manual overrides and other decoder profiles.
- Include both LiveTime selective import (including supported encrypted backups) and RC Scoring Pro Advantage ADT selective import (ZIP or extracted Data folder), with automatic file-format inspection, conflict review, source-preserving import and backup-first safety.
- Correct Personal Bests to reflect **race results**, not fastest individual laps: timed races rank by most completed laps then shortest elapsed time; fixed-lap races rank by shortest completion time after reaching the required lap target.
- Keep PB records separate for driver, class, race format, and configured race duration/target, preserving achieved date, track, event and session for a future live PB indicator.
- Exclude DNS/DNF/DQ, invalid results, missing official elapsed times, and unmatched race lengths from PB records.
- RCSP historical practice runs, track-record tables and unmatched lap crossings remain incomplete; review limitations before importing. Do not treat this draft as a lossless conversion.

## 0.17.1
- Restore Trackmate RC serial default to 9600 baud (8-N-1) following the independent RC interoperability reference and previous working setup. Preserve explicitly selected Trackmate baud overrides independently of other serial decoders; manual reconnection at 9600 may be required for systems that saved the earlier 115200 default.
- Detect IMRC Race Control SQLite, plain LiveTime SQLite, and RC Scoring Pro Advantage ADT backups (ZIP or extracted Data folder), then route supported sources to the appropriate import/recovery workflow.
- Store a per-driver/per-class fastest eligible lap index across completed race history and imported results; use official best-lap summaries when individual source crossings are missing. Retain source event/track and timestamp for future live PB indicators.
- Add read-only validation of RCSP ADT record layouts, counts, active/deleted rows, and column metadata, including ZIP backup support.
- Route recognized plain LiveTime databases directly to the existing selective import workflow.
- Reconstruct RCSP drivers, classes, class assignments, transponders, tracks, qualifying and main results, and validated per-lap crossings through the existing selective import/review/duplicate-handling workflow. Back up Race Control data before import.\n- Explicitly report incomplete RCSP lap associations and unsupported PracticeRuns/ADM payloads and require confirmation before importing. Unknown formats remain blocked.
- Add regression checks and preserve the published release.

## 0.17.0
- Prepare the next minor release from the complete 0.16.30 development source, preserving current LiveTime importing, race management, scoring, and installer functionality.
- Update the application package version and visible dashboard version to 0.17.0.
- Keep the existing published v0.16.30 release unchanged. This is a release preparation, pending validation and draft installer packaging.

## 0.16.30
- Review existing LiveTime matches before importing. Merge, keep an intentional copy, or discard incoming records one at a time, with merge-all and discard-all options. Keep saved results and transponder assignments protected.
- Show “No Event has started” in the Race tab until an event is selected.
- Choose tracks when importing a multi-track LiveTime backup, then select drivers, classes, and race history from those venues. Track Info keeps each imported venue and its records separate.
- Add Event class card zoom with a compact list at the smallest setting, support mouse-wheel scrolling over Track Records, and show progress while importing LiveTime data. Clarify Rating and SOF in the driver view and lineup reports.
- Preserve venue information from LiveTime imports, show records by track, and restore missing venue links when reimporting older history.
- Announce a new track and class record when the driver finishes and show it at the bottom of the race, pop-out, and web scoreboards.
- Search the Drivers tab by name or nickname. Show smaller clickable registration class cards and hide Rounds / Mains once an event ends.
- Ctrl-click multiple drivers or classes to delete them together while preserving completed race history.
- Open System.Data.SQLite RC4 and SQLCipher 4 LiveTime .db backups directly with the backup password. A matching SQLCipher unlock key also works. Preview in memory without a separate conversion.
- Right-click blank space in the Rounds / Mains lineup to reserve an empty race for a class missing from the selected round. Save the spot and add racers manually when they arrive.
- Show backup counts on selectable buttons for LiveTime drivers, classes, class assignments and personal transponders. Optionally import selected events with completed races and scored laps, and apply supported qualifying and mains settings to matching classes.
- Default Trackmate USB/Serial to 115200 baud and remember any manually selected Trackmate rate separately. I-Lap keeps its 9600 baud preset.
- Right-click an unknown Practice driver to assign the transponder to a driver and class without losing recorded laps. Rental assignments last for the current Practice session.

## 0.16.29
- Add a read-only LiveTime roster import in Saved Race Data. Preview active drivers, classes and transponder assignments, flag conflicts and back up Race Control before adding records. Show source history counts separately.
- Show combined runs as one compact race row with indented classes in the event schedule and Rounds / Mains. Count each combined run as one race in its round.
- Separate combined classes into labeled sections in the race table and Overall Position. Mark driver names with their class color so the names stay on one line.
- Show all participating classes in Overall Position on the dashboard, pop-out and local web board. Rank positions separately, including live and recalled combined races.
- Keep lineup changes pending across race, class and round changes and save them together. Move the Shift-click hint below the heat buttons and ask about discarding edits only when closing.
- Hold Shift and click saved unrun races in Rounds / Mains; release Shift to confirm a combined on-track run. Show a hand cursor, selection hint, combined parent and indented class/heat rows. Save Changes commits the grouping; right-click Separate restores independent races.
- Run combined heats with one start and separate positions, leader gaps, fastest laps, minimum lap times, lap-target finishes and stored class results. Keep qualifying, Series scoring, records and reports tied to each original class. Restart clears the combined run together.
- Identify combined drivers with their class name and a class-color number outline on the dashboard, pop-out and local web board; show all participating classes in the race heading. Keep live website uploads scoped to each class.
- Validate matching round, clock style, race format and duration/target, unique drivers/transponders, 30-car capacity and bump-up dependencies. Protect running/completed races and test combined results, recall, restart, persistence and lineup previews.

## 0.16.28
- Add Thanksgiving, Halloween and Christmas themes in Appearance, with matching dashboard, practice, pop-out and local web scoreboard palettes. Preserve custom dashboard colors and verify readable text, button contrast and saved theme selection.
- Drag driver names from the driver panel onto another eligible race, alongside the existing move button. Share validation for class, round, capacity, duplicate drivers/transponders and completed-race locks; cancelled or rejected drops leave both lineups unchanged. Save Changes commits the move.
- Drag saved unrun races to change their running order within the same qualifying round or mains round, with order saved across reopening and queue rebuilds. Group the lineup under distinct round headers and add individual mains-round filters. Preserve completed-race positions and heat identities.
- Merge lineup browsing and editing into one visible race list. Show drivers immediately for completed and unrun races, keep qualifying labels stable (Q1 • Heat 1/2), and display race status. Disable completed-race edits and reject completed-race deletion; restart from the dashboard before modifying a completed lineup.
- Switch pop-out and local web boards to the explicitly selected dashboard race, including race/round dropdowns and previous/next navigation.
- Keep the saved lineup overview visible while selecting a race to edit; only explicit round/class filters narrow the overview.
- Add right-click add/remove heat or main actions. Occupied heats offer unassign-all or move-all to an eligible race, validating capacity, duplicate drivers and transponders before changing the lineup. Event registrations remain intact.

## 0.16.27
- Add a compact Website Connection upload meter, saved MB budget (0 = unlimited), reset control, and completed-races-only publishing. Count attempted payload bytes including retries across restarts; pause uploads before exceeding the budget without interrupting local scoring.
- Redesign Track Info with separate Track Records, Track Details and Website Connection pages and a compact two-column details form.
- Show the fastest valid completed-race lap per driver in each class, with driver, lap time, event date and event. Default to all classes and top 10; choose any positive record count or one class.
- Print or save a PDF of the selected track records, with class headings and alternating rows. Records are scoped to the saved track; unassigned older events are flagged for assignment.

## 0.16.26

- Repeat all racers in the results table on every split report, and label lap-cell positions as P:#/LapTime.

- Split larger fields into balanced reports of up to eight drivers (10 = 5/5; 18 = 6/6/6), each with the complete race results above its grouped lap grid and prior qualifying while retaining overall positions.

- Label report breakouts as B-Out: #.### and verify all eight driver columns in the Windows print preview.

- Redesign completed-race reports with finishing results first, compact position/lap-time cells with pace underneath, and prior qualifying order at the bottom. Keep alternating rows, driver borders, best-lap emphasis and breakout annotations; repeat table headings across pages.
- Rename the dashboard EXPORT button to REPORTS. Include results and prior qualifying in CSV exports, and retain completed race duration for future pace reports.

## 0.16.25

- Give breakout name highlighting priority over normal line crossings on the dashboard, pop-out and web display, using the configured breakout color and duration. Restore normal crossing highlighting when the breakout expires.

## 0.16.24

- Emphasize class, qualifying round and heat on every lap-report page. Show breakout attempts marked ** above the valid lap time with a fraction-style divider, retaining the attempted lap number and uncompleted attempts.

- Print/export lap times as a driver-column matrix: up to eight drivers, nickname/last-name headers, alternating lap rows, column borders and repeated page headers. Remove elapsed time.
- Qualifying lineup sheets show each driver’s best completed qualifying laps/time with the winning round in parentheses.

- Add minimum lap time and tone/leader/individual clock styles to FMT, including live updates; retain recorded laps and save the minimum-lap override per session/round.

- Allow FMT changes for loaded and running races without resetting laps or clocks; apply to unfinished races in the class/round while preserving completed results and already-finished drivers.

- Keep dashboard side buttons fully visible with a correctly sized tab rail.
- Add EXPORT on the race dashboard: choose a completed race and all drivers or one driver; print/PDF or export CSV with each lap time and elapsed time.

- Give event driver/class registration most of the available height and reduce the bottom rounds area; add a draggable divider to resize both areas live.
- Display class choices as compact wrapping cards with class colors, selection state and transponder fields; move Add Class into the heading.

- Default Events to driver-first registration: select a driver, then choose classes and separate event transponder numbers.
- Add Settings > Application > Use class-first registration to restore the previous view without changing event data.
- Preserve built rounds and race results on roster additions/removals; warn to update race placement in Open Rounds / Mains and reprint the lineup.

- Redesign lineup sheets with a compact logo/round header, vivid race-number badges, neutral tables and stronger alternating gray rows.
- Replace Seed with class-specific SOF/SR performance/safety ratings and mark unrated starting values.
- Show each driver's latest earlier completed round, laps and total time using driver identity across car-number changes.
- Upgrade saved pastel class selections to corresponding vivid colors while retaining readable black text.

- Reset one or all rental nicknames to the saved naming defaults, preserving assigned label numbers and history.
- Print rental nicknames without a Rental prefix, using the same typography as personal transponders.
- Expose Reopen Event for completed race days, preserving completed race results and archives.
- Size the new-event form to its contents and give application dialogs themed title bars with a close button, excluding the spectator pop-out.
- Select visible pastel class color swatches that remain legible with black print text.
- Add Race Control branding at the top left of printouts, left-side race numbers, colored class headings and softly tinted alternating table rows.

- Remove rentals from inventory while preserving event data and usage history; readding the same number reconnects its history.
- Shorten existing automatic Transponder number*** nicknames to number***, preserving custom nicknames.

- Add the Transponder navigation tab with an unlimited rental inventory, manual entry, and drag/drop from the live decoder feed.
- Default rental nicknames to number***; support Red #1 / Black #5 style labels, persistent defaults for every addition, a Random color option, and reusable custom nickname patterns.
- Reject duplicate physical transponder numbers, nicknames and color/number combinations.
- Keep assignment details in history, captured from event rosters; remove event/class/racer assignment controls and the Assigned to column from the inventory page.
- Allocate the lowest unused label number starting at 1 across every color, rejecting duplicates.
- Keep inventory sizing standard; print rental nicknames alongside unchanged personal transponder numbers.
- Retain assignment snapshots with racer, class and event date, showing the last three events actually used or all history even after skipped weeks, renames or event deletion.
- Persist rentals/history in the existing atomic database snapshot and backups, capture event-roster assignments, and validate uniqueness before saving.
- Add rental regression tests and Windows-rendered theme previews with page interaction checks.

## 0.16.23

- Replace the stock already-installed confirmation with a fully themed Race Control installer, including Install, Update, Repair, completion and error screens.
- Show installed and installer versions, installation location, Open install folder, Cancel, and the matching action in one branded window.
- Run the embedded installation engine silently only after confirmation; prevent downgrades and ask users to close Race Control before installing.
- Keep the single self-contained RaceControl-Installer.exe download and existing application/update/data locations.
- Validate fresh install, update from 0.16.22, and repair on Windows; export actual confirmation-window previews.

## 0.16.22

- Replace the installer logo-only splash with a branded dark Race Control panel, approved RC artwork, red accents, version, and installation text.
- Show a looping activity indicator while installing or updating, without displaying a simulated completion percentage.
- Keep the single RaceControl-Installer.exe download and existing data protection.
- Export a Windows-rendered preview and verify every installer animation frame during packaging.

## 0.16.21

- Embed the approved RC icon in every application build.
- Refresh existing Race Control desktop and Start menu shortcuts with a dedicated artwork-specific icon path, avoiding stale executable icon caches.
- Repair shortcuts that still launch the previous installation when opening the new installed app; keep race data untouched.
- Verify the application and installer use identical icon files during packaging.

## 0.16.20

- Apply the approved white RC monogram with black outline and blue accent to the shortcut, app, installer and loading screen.
- Generate desktop/taskbar icon sizes from 16 to 256 pixels so Windows uses a clear square icon instead of a tiny horizontal logo.
- Rename the downloadable installer to RaceControl-Installer.exe and update build artifacts, release checks, documentation and installation messages.
- Keep this release unsigned with no paid signing service configured; document download checksums and possible Windows publisher/reputation warnings.

## 0.16.19

- Move Update and Data tools from the main header into Settings > Application.
- Save current settings before opening either tool; block maintenance during active races or practice.
- Keep hover tooltips readable in light, dark and blue themes.
- Keep disabled and empty Series lists themed and show an explanatory empty-state message.
- Remove the logo's white rectangle and add a white outline in dark and blue themes.
- Apply the selected theme to the loading screen and retain Windows-rendered logo previews for each theme.
- Use this single changelog for source history and GitHub release notes.

## 0.16.18

- Original IMRC lettermark with new RACE CONTROL wording in dashboard, loading screen, installer and application icon.
- Self-contained Setup.exe: download only this file; remaining release assets power online updates.
- Separate installation and user-data directories so repair/reinstallation cannot remove the race database.
- Copy surviving legacy race data, settings, audio, results and archives without deleting originals.
- Automatic SQLite backups, backup before updates, and DATA backup/restore controls.
- Invalid database/settings errors preserve data instead of silently resetting saved state.
- Regression checks simulate old installation removal, WAL migration, backup recovery and rejected corrupt restores.
