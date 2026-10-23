# Changelog

Keep all release history in this file. Add each new version at the top; do not create version-specific changelog files.

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
