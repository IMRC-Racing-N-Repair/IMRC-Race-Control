# IMRC Race Control

Windows race scoring software by IMRC Racing N Repair.

## Installation and updates

The first installer-enabled version is **0.16.17**. Install using the Setup.exe attached to a published GitHub release. Installed copies check for stable updates after launch; updates can also be checked manually using the refresh arrow in the top bar. Race and practice sessions block application updates.

For complete release and installer instructions, see [GITHUB_RELEASES.md](GITHUB_RELEASES.md).

## Development

Requires Windows and the .NET 10 SDK for interactive development.

```powershell
dotnet build src/IMRCRaceControl.App/IMRCRaceControl.App.csproj
dotnet run --project src/IMRCRaceControl.App/IMRCRaceControl.App.csproj
```

## Release process

1. Update the app version and push source changes.
2. Open **Actions → Build draft release → Run workflow**.
3. The Windows workflow checks the code and generates an installer plus update packages.
4. Review and publish the resulting draft release when ready.

Normal source pushes run build checks without publishing. Drafts and prereleases are excluded from stable app updates. Existing ZIP/source builds need the first Setup.exe installation once before automatic updates are available.

## Features

- Race-day event, qualifying, heat and main lineup management.
- Decoder and manual passing support, lap timing, results and Series scoring.
- Practice session statistics and history.
- Spectator pop-out and local browser webcast with shared display preferences.
- Adjustable race audio, themes, driver display zoom and NEXT UP scrolling.

Race data and preferences are stored outside the installed program directory in LocalAppData.

See [VALIDATION.md](VALIDATION.md) for checks completed and Windows acceptance tests still required.
