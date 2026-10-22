# Changelog

Keep all release history in this file. Add each new version at the top; do not create version-specific changelog files.

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
