# IMRC Race Control

Windows race scoring software by IMRC Racing N Repair.

## Installation and updates

Use **0.16.18 or later** for the protected data location. Download **RaceControl-Installer.exe only** from a published GitHub release; it installs the runtime and all required program files. If you installed 0.16.17, close the old app and run the new RaceControl-Installer.exe once before uninstalling anything. Installed copies check for stable updates after launch; updates can also be checked manually in **Settings → Application → Check for updates**. Race and practice sessions block application updates.

This release is unsigned; Windows may show an unknown publisher or app reputation warning. No paid code-signing service is configured.

For complete release and installer instructions, see [GITHUB_RELEASES.md](GITHUB_RELEASES.md). Release history is kept in [CHANGELOG.md](CHANGELOG.md).

Application update checks and database backup/restore tools are in **Settings → Application**.

## Development

Requires Windows and the .NET 10 SDK for interactive development.

```powershell
dotnet build src/IMRCRaceControl.App/IMRCRaceControl.App.csproj
dotnet run --project src/IMRCRaceControl.App/IMRCRaceControl.App.csproj
```

## Features

- Race-day event, qualifying, heat and main lineup management.
- Decoder and manual passing support, lap timing, results and Series scoring.
- Practice session statistics and history.
- Spectator pop-out and local browser webcast with shared display preferences.
- Adjustable race audio, themes, driver display zoom and NEXT UP scrolling.

Race data and preferences are stored outside the installed program directory in LocalAppData.

