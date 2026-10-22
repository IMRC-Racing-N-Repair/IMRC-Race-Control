# IMRC Race Control v0.14.3 architecture

### Dashboard check-in, sorting and print layout (v0.13.5)

`RaceCheckInState` holds only the current visit's checked cars. Loading a different session clears that set; restarting explicitly clears the same session. False start only resets the race engine, preserving check-in. Completed recall uses stored result rows for its read-only display. The start guard lists unchecked enabled racers and requires an explicit override to start without them.

Event-grid sort descriptions always lead with numeric stage and round, then the requested column within that round. `RaceSchedule.DisplayOrder` supplies matching default report order without changing the event queue. The lineup renderer measures rows against printable page dimensions, repeats the full round heading on every fixed page and repeats race/table headings for continuations. Other report types retain their FlowDocument layout. Race selector close/selection queues keyboard focus back to a non-editing dashboard surface after WPF finishes popup focus handling.


### Round selection and racer-facing numbers (v0.13.4)

`RaceSchedule` assigns persistent `EventSessionDefinition.RaceNumber` values by stage, class and heat. `QueueOrder` remains the unique event-wide execution order. Saved numbers are reserved before new slots are allocated, preventing later rounds or late-added heats from renumbering existing slots. `DashboardRoundChoice` uses qualifying `Round` or main `MainLeg`; the two dashboard selectors share existing live-race/save guards. Builder previews calculate numbers on detached session copies. SQLite retains the number in both JSON and an additive `EventSessions.RaceNumber` column.


```text
Classes / Drivers / Race Day Events
               |
               v
          Session Roster
               |
               v
Keyboard / Manual Buttons / future decoded hardware passing
               |
               v
            RaceEngine
               |
        normalized scoring
               |
               +--> live standings / metrics
               +--> lap history
               +--> CSV / JSON results

Hardware transport layer
  IByteTransport
    |- SerialUsbTransport
    |- TcpTransport
    |- SimulatorTransport
               |
               v
future IDecoderProtocol adapters
    |- MYLAPS / AMB
    |- i-Lap
    |- additional decoder protocols
               |
               v
normalized passing -> same RaceEngine pipeline
```

## Timing design

Three start modes are modeled independently of the transport source.

### Tone
Every racer is assigned the same start timestamp at START.

### Leader crossing
The session is armed. The first configured racer (Car 1) is the leader. The leader's first accepted crossing becomes the common start timestamp for all racers. That crossing is a start marker and not a completed lap.

### Individual crossing
The session is armed. Each racer's first accepted crossing starts only that racer's clock. That crossing is a start marker and not a completed lap. Lap elapsed time, expiration, final-lap state, and finish are all calculated from that personal start timestamp.

This prevents a racer who reaches the line later in staggered qualifying from losing race time relative to the first racer.

## Scoring integrity rules

1. A finished racer cannot receive another scored lap.
2. A completed session rejects all additional passings.
3. Minimum lap filtering is applied from the racer's start marker or prior completed lap.
4. Raw decoder bytes are logged before future protocol parsing.
5. Decoder transports never own race-scoring logic.
6. Manual entry and future decoder entry use the same race engine.
7. False Start clears all timing/laps from the current attempt.
8. END RACE immediately stops scoring.


## Durable event history (v0.5)
RaceDayEventDefinition is now the durable race-day record. Each EventSessionDefinition stores lifecycle state, entrants, final standings, and lap records. Ending an event creates an additional standalone JSON archive. Driver IDs and class IDs remain stable across events so a future series layer can aggregate event history without scraping CSV files.

## v0.7 race-day selection UX

Class and driver inclusion for an event is represented by RaceDayEventDefinition.ClassIds and RaceDayEventDefinition.DriverAssignments. The checklist UI is a projection of those durable collections, not a second source of truth. Search only filters the displayed EventDriverChoice rows and never alters event membership.

DriverProfile now includes optional FirstName and LastName while preserving the existing Name field for backward compatibility and stable display behavior.


## v0.9.2 persistence architecture

`RaceControlDatabaseService` owns the authoritative SQLite application state in
`%LOCALAPPDATA%\IMRCRaceControl\RaceControl.db`.

The database combines:

- a single atomic serialized `AppState` recovery snapshot
- normalized/queryable driver, class, assignment, event, session, lap, breakout, ranking and series tables
- the existing PracticeStints/PracticeLaps tables

`settings.json` is retained as a fallback import/backup only.

Startup order:
1. Load settings.json as fallback.
2. Initialize SQLite.
3. If SQLite contains AppState, load it as authoritative.
4. Otherwise import the fallback state into SQLite once.
5. Build observable UI collections from the authoritative state.

Save order:
1. Capture live collections into AppSettings.
2. Transactionally save AppState + query tables to SQLite.
3. Save settings.json as a fallback backup.

Database save exceptions are caught/logged so a persistence problem cannot
silently terminate the race operator UI.


## v0.10 race-format and mains architecture

`ClassDefinition` now owns the scoring format and main rule set. Main rules are
top-down (A, B, C...) and `BumpInsFromLower` describes reserved positions in the
named main.

`RoundMainBuilderDialog` converts the rule set into direct qualifier positions.
The last actually-used main receives no bump-ins because no lower main exists.
`EventSessionDefinition` persists the resulting main capacity and bump routing.

When a lower main completes, `ApplyMainBumpUps` copies the configured number of
top finishers into the next higher planned main before application state is
committed to SQLite.

Unknown decoder tags are held in a transient UI queue because an unassigned tag
is not yet valid racing data. Once the operator assigns it, the transponder
relationship and resulting normal timing/check-in records are persisted through
the standard application-state/database path.


## v0.12.0 roster sizing

Driver/class/event assignment collections are not bounded by an individual race
size. Race construction applies the per-heat/per-main size only while partitioning
the class field into sessions.

The same per-race capacity is enforced by automatic builders, manual heat/main
editing, ad-hoc roster loading and Race Dashboard roster additions so those paths
cannot disagree about the supported race size.


## v0.11.4 builder workflow

`RoundMainBuilderDialog` opens on the full event-class scope. The Round field
continues to be calculated from saved qualifying history. Generation is now one
mode-aware command rather than separate "Next Round" and "Generate from Roster"
actions.

Saved-session loading remains a distinct workflow because it can intentionally
select a historical class/round for review or rebuilding.


## v0.11 main-series model

A main is identified by `EventSessionDefinition.Heat` (A=1, B=2...) and may contain multiple `MainLeg` sessions. `MainRunCount` and `MainCountBest` are copied from the class `MainRuleDefinition`. `MainSortOrder` records the seeding method used when the main was generated.

Bump-ups are resolved at the main-series level, not the individual-leg level. A lower main must finish all scheduled legs before aggregate standings are calculated. Bump finishers are then copied into every unstarted leg of the next higher main.


## v0.11.4 builder state separation

`RoundMainBuilderDialog` now treats `HeatPlans` as draft/preview state and
`RaceDayEventDefinition.Sessions` as the already-saved event state.

Normal Build/Class/Sort changes operate only on the preview. Existing event
sessions are loaded into the preview only through the explicit
`LOAD / EDIT SELECTED` workflow.

Saved-session deletion is an explicit direct event mutation. The dialog tracks
deleted session IDs so `MainWindow` can clear a stale loaded race, recalculate
rankings, rebuild the remaining queue, refresh Race Day, and persist SQLite even
when the dialog is closed without a preview rebuild.


## v0.11.4 builder display labels

`HeatPlan.Name` and `EventSessionDefinition.Name` remain the full persisted names.
The builder now exposes separate compact `SessionDisplay` values so UI density
can improve without changing stored session identity or other Race Day screens.

Qualifying preview labels are recalculated from the current sibling-heat count,
and saved qualifying labels derive their heat index from the remaining sessions,
so deleting a saved heat cannot produce invalid labels such as `Heat 3/2`.
