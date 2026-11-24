param([Parameter(Mandatory=$true)][string]$Repository, [string]$LiveTimePasswordResource,
    [ValidateSet('All','Prepare','Brand','Dashboard','Highlights','Rentals','Reports','Registration','Records','Lineups','Spectator','Package')][string]$Phase = 'All')
$ErrorActionPreference = 'Stop'
$liveTimeArgs = @()
if ($LiveTimePasswordResource) {
    $bundle = (Resolve-Path -LiteralPath $LiveTimePasswordResource).ProviderPath
    $liveTimeArgs = @("-p:LiveTimePasswordResource=$bundle")
}
Set-Location (Join-Path $PSScriptRoot '..')
if ($Repository -notmatch '^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$') { throw 'Use https://github.com/OWNER/REPOSITORY' }
$Repository = $Repository.TrimEnd('/')
$project = 'src/IMRCRaceControl.App/IMRCRaceControl.App.csproj'
[xml]$xml = Get-Content $project
$version = [string]$xml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must use major.minor.patch' }
function Checked([string[]]$Arguments) { & dotnet @Arguments; if ($LASTEXITCODE -ne 0) { throw "dotnet command failed: $($Arguments[0])" } }
if ($Phase -in @('All','Prepare')) {
# Exercise upload limits, durable metering and results-only mode before packaging.
Checked @('run','--project','tests/LapCalloutChecks/LapCalloutChecks.csproj','-c','Release','--','--online-data-only')
Checked @('run','--project','tests/LapCalloutChecks/LapCalloutChecks.csproj','-c','Release','--','--livetime-import-only')
Checked @('tool','restore')
# Clean generated payloads so a release never includes stale files.
Remove-Item 'artifacts/publish','artifacts/releases','artifacts/branding' -Recurse -Force -ErrorAction SilentlyContinue
Checked (@('publish',$project,'-c','Release','-r','win-x64','--self-contained','true','-o','artifacts/publish',"-p:UpdateRepository=$Repository",'-p:PublishSingleFile=false') + $liveTimeArgs)
}
# Each isolated preview has a CI-only watchdog and streams checkpoint lines.
# This never runs in the normal race-control application.
$branding = Join-Path (Get-Location) 'artifacts/branding'
$appExe = Join-Path (Get-Location) 'artifacts/publish/IMRCRaceControl.exe'
function Invoke-Preview([string]$Executable, [string[]]$Arguments, [string]$Label) {
    Write-Host "Starting $Label"
    $checkpoint = Join-Path $branding 'RentalPreviewProgress.txt'
    $seen = if (Test-Path $checkpoint) { @(Get-Content $checkpoint).Count } else { 0 }
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -PassThru
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        do {
            $finished = $process.WaitForExit(1000)
            if (Test-Path $checkpoint) {
                $lines = @(Get-Content $checkpoint)
                if ($lines.Count -gt $seen) {
                    $lines | Select-Object -Skip $seen | Write-Host
                    $seen = $lines.Count
                }
            }
            if (-not $finished -and $clock.Elapsed.TotalMinutes -ge 5) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                throw "$Label failed to exit within the CI preview watchdog; see the last checkpoint above."
            }
        } until ($finished)
        Write-Host "$Label completed with exit code $($process.ExitCode)"
        if ($process.ExitCode -ne 0) {
            foreach ($name in @('Brand-UI-Error.txt','Transponder-UI-Error.txt')) {
                $details = Join-Path $branding $name
                if (Test-Path $details) { Get-Content $details | Write-Host }
            }
            throw "$Label failed (exit $($process.ExitCode))"
        }
        return $process.ExitCode
    }
    finally { $process.Dispose() }
}
if ($Phase -in @('All','Brand')) {
    $null = Invoke-Preview $appExe @('--export-brand',"`"$branding`"") 'Brand UI checks'
}
foreach ($part in @('Dashboard','Highlights','Rentals','Reports','Registration','Records','Lineups','Spectator')) {
    if ($Phase -in @('All',$part)) {
        $null = Invoke-Preview $appExe @('--export-rentals',"`"$branding`"",$part.ToLowerInvariant()) "$part UI checks"
    }
}
if ($Phase -notin @('All','Package')) { return }
if (-not (Test-Path (Join-Path $branding 'Dashboard-Startup-Checks.txt'))) { throw 'Missing dashboard XAML startup validation' }
foreach ($theme in @('Light','Dark','Blue')) {
    if (-not (Test-Path (Join-Path $branding "Transponder-$theme.png"))) { throw "Missing Transponder preview for $theme" }
}
$icon = Join-Path $branding 'IMRC-Race-Control.ico' 
# The renderer validates animation frame dimensions and 80ms timing before packaging.
$splash = Join-Path $branding 'Installer-Splash.gif'
if (-not (Test-Path $splash)) { throw 'Branded installer animation was not rendered' }
if (-not (Test-Path $icon)) { throw 'Brand icon was not rendered' }
# Verify the committed application icon matches the Windows-rendered installer icon.
if ((Get-FileHash $icon).Hash -ne (Get-FileHash "src/IMRCRaceControl.App/Assets/RC.ico").Hash) { throw "Application and installer icons differ" }
# The already published application used the committed icon; the exact
# hash comparison above proves the rendered installer icon is identical.
Checked @('tool','run','vpk','--','pack','--packId','IMRCRaceControl.App','--packVersion',$version,'--packDir','artifacts/publish','--mainExe','IMRCRaceControl.exe','--packTitle','IMRC Race Control','--packAuthors','IMRC Racing N Repair','--icon',$icon,'--splashImage',$splash,'--splashProgressColor','None','--outputDir','artifacts/releases')
$setup = @(Get-ChildItem 'artifacts/releases/*Setup.exe')
if ($setup.Count -ne 1) { throw 'Expected exactly one installer' }
# The downloadable EXE owns the complete confirmation/progress/error UI. The
# embedded Velopack setup runs silently only after explicit Install/Update/Repair.
$payloadDirectory = Join-Path (Get-Location) 'artifacts/installer-payload'
New-Item $payloadDirectory -ItemType Directory -Force | Out-Null
$payload = Join-Path $payloadDirectory 'RaceControl-Setup.exe'
Move-Item $setup[0].FullName $payload -Force
Checked @('publish','src/IMRCRaceControl.Installer/IMRCRaceControl.Installer.csproj','-c','Release','-r','win-x64','--self-contained','true','-o','artifacts/installer-shell',"-p:Version=$version","-p:InstallerPayload=$payload",'-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:EnableCompressionInSingleFile=true')
Copy-Item 'artifacts/installer-shell/RaceControl-Installer.exe' 'artifacts/releases/RaceControl-Installer.exe'
$installerExit = Invoke-Preview (Join-Path (Get-Location) 'artifacts/releases/RaceControl-Installer.exe') @('--export-ui',"`"$branding`"") 'Installer UI check'
if ($installerExit -ne 0) { throw 'Installer confirmation UI validation failed' }
# Portable source bundles are not needed by users or by the updater.
Get-ChildItem 'artifacts/releases/*Portable.zip' | Remove-Item
# Internal vpk upload inventory still uses its generated names; gh uploads the final filenames.
Remove-Item 'artifacts/releases/assets.win.json' -ErrorAction SilentlyContinue

Get-ChildItem 'artifacts/releases' -File | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  $([IO.Path]::GetFileName($_.Path))" } | Set-Content 'artifacts/releases/SHA256SUMS.txt'
Write-Host "Installer and update files ready in artifacts/releases (version $version)."

# Rental previews exercise inventory removal, nickname resets, class colors and themed dialogs.
if (-not (Test-Path (Join-Path $branding "Transponder-Print.png"))) { throw "Missing rental print preview" }

foreach ($dialog in @('EventEditorDialog','ClassEditorDialog','RentalAppearanceDialog')) { if (-not (Test-Path (Join-Path $branding "Dialog-$dialog.png"))) { throw "Missing themed dialog preview: $dialog" } }

if (-not (Test-Path (Join-Path $branding "Transponder-Print-Continuation.png"))) { throw "Missing long-lineup pagination preview" }

if (-not (Test-Path (Join-Path $branding "Event-Driver-Registration.png"))) { throw "Missing compact class-card event registration preview" }

if (-not (Test-Path (Join-Path $branding "Dialog-CompletedRaceExportDialog.png"))) { throw "Missing completed race export preview (breakout lap matrix build)" }
if (-not (Test-Path (Join-Path $branding "Dialog-ReportPrintDialog.png"))) { throw "Missing completed lap report preview" }
if (-not (Test-Path (Join-Path $branding "Lap-Matrix-8-1.png"))) { throw "Missing eight-driver lap matrix preview" }
if (-not (Test-Path (Join-Path $branding "Lap-Matrix-8-2.png"))) { throw "Missing continued lap matrix preview" }

if (-not (Test-Path (Join-Path $branding "Breakout-Highlight-Checks.txt"))) { throw "Missing scoped-resource breakout highlight checks" }

if (-not (Test-Path (Join-Path $branding "Race-Report-Checks.txt"))) { throw "Missing race results and qualifying pagination checks" }

# Report breakout labels use decimal seconds: B-Out: #.###.
if (-not (Test-Path (Join-Path $branding "Breakout-Report-8-Checks.txt"))) { throw "Missing eight-driver B-Out report checks" }

if (-not (Test-Path (Join-Path $branding "Balanced-Report-Checks.txt"))) { throw "Missing balanced 10/18-driver race report checks" }

if (-not (Test-Path (Join-Path $branding "Track-Records-Checks.txt"))) { throw "Missing track records UI and print checks" }
if (-not (Test-Path (Join-Path $branding "Full-Results-Report-Checks.txt"))) { throw "Missing complete-results split report checks" }

if (-not (Test-Path (Join-Path $branding "Lineup-Overview-Checks.txt"))) { throw "Missing persistent lineup overview checks" }
if (-not (Test-Path (Join-Path $branding "Lineup-Overview.png"))) { throw "Missing full lineup overview preview" }
if ((Get-Content (Join-Path $branding "Lineup-Overview-Checks.txt") -Raw) -notmatch "^PASS:") { throw "Lineup overview validation did not pass" }
if (-not (Test-Path (Join-Path $branding "Unified-Lineup-Checks.txt"))) { throw "Missing unified lineup and completed-race protection checks" }
if (-not (Test-Path (Join-Path $branding "Race-Order-Checks.txt"))) { throw "Missing grouped lineup race ordering checks" }
if (-not (Test-Path (Join-Path $branding "Draft-Heat-Drag-Checks.txt"))) { throw "Missing unsaved heat drag regression checks" }
if (-not (Test-Path (Join-Path $branding "Driver-Drag-Checks.txt"))) { throw "Missing driver drag and button compatibility checks" }

if (-not (Test-Path (Join-Path $branding "Seasonal-Theme-Checks.txt"))) { throw "Missing seasonal theme contrast and persistence checks" }
foreach ($theme in @('Thanksgiving','Halloween','Christmas')) {
    if (-not (Test-Path (Join-Path $branding "Transponder-$theme.png"))) { throw "Missing seasonal preview: $theme" }
    if (-not (Test-Path (Join-Path $branding "Track-Records-$theme.png"))) { throw "Missing seasonal records preview: $theme" }
}

# Combined-race builds must include Shift multiselection, the full-width header, grouping and separation WPF smoke preview.
# Require previews of the compact lineup editor and the event schedule.
foreach ($file in @("Combined-Lineup.png", "Combined-Lineup-Checks.txt", "Combined-Event-Rounds.png")) {
    if (-not (Test-Path (Join-Path $branding $file))) { throw "Missing combined race schedule check: $file" }
}

if (-not (Test-Path (Join-Path $branding "Lineup-Draft-Checks.txt"))) { throw "Missing multi-scope lineup draft checks" }

if (-not (Test-Path (Join-Path $branding "Combined-Class-Displays.png"))) { throw "Missing combined class display preview" }

