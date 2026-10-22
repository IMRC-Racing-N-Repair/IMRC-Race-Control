# IMRC driver rankings — v0.14.1

Open **Drivers → Ratings / Safety** and select a class, or All classes. Select a driver to see their race history and field strength. **Drivers → Profiles** displays a selectable permanent UUID, reserved for the future driver system. UUID, performance rating, SOF, Safety and their controls are visible only within Drivers; there is no separate Rankings navigation item and none are added to dashboards, standings, mirror views or printouts. Existing GUID-based driver IDs are retained; changing a nickname, name or transponder does not reset identity. Legacy imported string IDs receive a deterministic UUID representation without rewriting database references.

Rankings are local to this Race Control database. There is no central online account or cross-track synchronization in this release. Copying a driver's UUID is not an import/merge feature.

## Eligible results

Rankings rebuild from retained, completed **mains** in chronological start order. Each main/leg counts once, with at least two racers who recorded laps. Qualifying, Practice, incomplete races, DNS/zero-lap entries and unknown-driver placeholders do not count. Cars are matched to saved roster DriverId values, never by display name or transponder. Ambiguous duplicate car/driver identities are excluded rather than guessed.

Every class has separate ratings. Reopening the app does not award a race twice. Correcting results, restarting a race, deleting a result or deleting an event changes the rebuilt rankings. Keep historical events to preserve ranking history. Drivers removed from the directory can remain in historical standings using their saved roster identity.

## Performance rating and Strength of Field

Each driver/class starts at **1500**. Strength of Field (SOF) is the arithmetic mean of the starters' ratings immediately before that main. Stronger opponents affect the performance adjustment through pairwise expected outcomes:

- Expected result against another driver: `1 / (1 + 10 ^ ((opponentRating - driverRating) / 400))`.
- Actual result: win = 1, tie = 0.5, loss = 0, using the saved finishing position.
- Rating change: `32 × average(actual - expected)` over the other starters. Ratings are bounded to 100–4000.

All changes use pre-race ratings simultaneously. Two equally rated racers transfer 16 points for a decisive result. Beating a stronger racer earns more than beating a weaker racer.

Performance tiers: fewer than five mains = Provisional; otherwise Club below 1300, Sport 1300–1599, Advanced 1600–1899, Expert 1900–2199, Elite 2200+. These are IMRC's initial local rules, not iRacing's formula.

## Safety from consistency

Lap timing cannot prove a crash, contact, fault or off-track incident. The Safety score is a **consistency-based estimate**. Congestion, a missed read, stopping for a repair or battery trouble can produce the same slow lap as an accident.

Default rules:

1. Exclude lap 1 from each main for start/grid effects. Use positive, finite, accepted lap records after corrections; rejected/breakout readings are not laps.
2. Require ten remaining laps in that main before it contributes to Safety.
3. Find that driver's median lap. Flag a lap only when it is more than `max(25% of the median, 1 second)` slower than the median. Each flagged lap is one estimated incident, not a verified accident. A slower but consistent driver is not penalized for absolute pace.
4. Give each eligible lap a pace-normalized consistency contribution: `quality = clamp(1 - abs(lap - median) / median, 0, 1)`. A lap matching the typical pace earns 1; deviations earn less. Consistency affects the base score even below the accident threshold.
5. Keep the most recent 200 eligible laps per driver/class. Calculate `clamp((300 + 5 × sum(quality)) / (counted laps + 100) - accidentPenalty × flagged laps, 0, 5)`. The prior starts new drivers at **3.00**. Each estimated accident has an additional **0.25-point** penalty by default, adjustable from 0.05 to 2.00. More consistent laps build the score; an accident reduces both its consistency contribution and adds the explicit penalty. Old contributions and penalties expire together as their laps leave the window.

The per-race **SR CHANGE** column shows the resulting Safety change. A race with many clean laps can still gain overall Safety despite a penalty; it always scores lower than the same race without that accident.

The five controls change slowdown percentage, minimum extra seconds, minimum eligible laps after lap 1, recent-lap window and penalty per accident. **Save / Recalculate** applies them to all retained results. Consistency shown in history is `100 × (1 - standard deviation / mean lap)`, bounded to 0–100; the safety flags use the median-based rule above.

Safety tiers: fewer than 50 counted laps = Provisional; otherwise Developing below 2, Steady 2–2.99, Clean 3–3.99, Very clean 4+. The scores stay separate from performance ranking.

To review a race, select its row, check **Exclude this race from Safety**, enter a reason and save. To restore it, uncheck that option and save with a note. This affects only that driver's safety contribution from that race, not performance, finish order, other drivers or stored lap times. The latest review decision and note are persisted; this is not a full multi-user audit log.

## Unknown practice drivers

An unregistered transponder starts an **Unknown driver** card displaying its tag number. The first crossing starts the clock; subsequent valid crossings count laps, with the normal statistics, callout toggle, idle handling and saved history. Different tags remain separate. No fake permanent driver is created and Practice does not affect rankings.

If a tag is assigned while its unknown card is active, the next crossing changes that card to the driver and retains its laps. Stints already saved under Unknown driver remain in history under the original tag; they are not silently assigned retroactively.

## Profile class rows (v0.14.2)

Drivers → Profiles displays Rating, SOF and Safety beside the transponder for each class assignment. Rating is that driver's earned performance rating; SOF is the strength of the field in their most recent eligible main. Safety is their current class Safety score. A driver without eligible results shows the starting Rating 1500 and Safety 3.00, with SOF unavailable. The Ratings / Safety page retains the detailed history and review controls. All of these displays stay within Drivers.
