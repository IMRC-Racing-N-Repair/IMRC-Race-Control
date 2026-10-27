param([Parameter(Mandatory=$true)][string]$Repository)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
if ($Repository -notmatch '^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$') { throw 'Use https://github.com/OWNER/REPOSITORY' }
$Repository = $Repository.TrimEnd('/')
$project = 'src/IMRCRaceControl.App/IMRCRaceControl.App.csproj'
[xml]$xml = Get-Content $project
$version = [string]$xml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must use major.minor.patch' }
function Checked([string[]]$Arguments) { & dotnet @Arguments; if ($LASTEXITCODE -ne 0) { throw "dotnet command failed: $($Arguments[0])" } }
Checked @('tool','restore')
# Clean generated payloads so a release never includes stale files.
Remove-Item 'artifacts/publish','artifacts/releases' -Recurse -Force -ErrorAction SilentlyContinue
Checked @('publish',$project,'-c','Release','-r','win-x64','--self-contained','true','-o','artifacts/publish',"-p:UpdateRepository=$Repository",'-p:PublishSingleFile=false')
# Render the native app brand to PNG/ICO on the Windows runner; no external image service is required.
$branding = Join-Path (Get-Location) 'artifacts/branding'
$renderer = Start-Process -FilePath (Join-Path (Get-Location) 'artifacts/publish/IMRCRaceControl.exe') -ArgumentList @('--export-brand',"`"$branding`"") -Wait -PassThru
if ($renderer.ExitCode -ne 0) { throw "Brand asset rendering failed (exit $($renderer.ExitCode))" }
$rentalPreview = Start-Process -FilePath (Join-Path (Get-Location) 'artifacts/publish/IMRCRaceControl.exe') -ArgumentList @('--export-rentals',"`"$branding`"") -Wait -PassThru
if ($rentalPreview.ExitCode -ne 0) {
    $details = Join-Path $branding 'Transponder-UI-Error.txt'
    if (Test-Path $details) { Get-Content $details | Write-Host }
    throw 'Transponder page UI checks failed'
}
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
# Rebuild with the same icon used by RaceControl-Installer.exe and the updater.
Checked @('publish',$project,'-c','Release','-r','win-x64','--self-contained','true','-o','artifacts/publish',"-p:UpdateRepository=$Repository","-p:ApplicationIcon=$icon",'-p:PublishSingleFile=false')
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
$preview = Start-Process -FilePath (Join-Path (Get-Location) 'artifacts/releases/RaceControl-Installer.exe') -ArgumentList @('--export-ui',"`"$branding`"") -Wait -PassThru
if ($preview.ExitCode -ne 0) { throw 'Installer confirmation UI validation failed' }
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

if (-not (Test-Path (Join-Path $branding "Breakout-Report-8-Checks.txt"))) { throw "Missing eight-driver B-Out report checks" }
