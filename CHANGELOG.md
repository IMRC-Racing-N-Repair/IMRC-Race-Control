# Changelog

Keep all release history in this file. Add each new version at the top; do not create version-specific changelog files.

## 0.16.26

- Split larger fields into balanced reports of up to eight drivers (10 = 5/5; 18 = 6/6/6), each with its own results, lap grid and prior qualifying while retaining overall positions.

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
