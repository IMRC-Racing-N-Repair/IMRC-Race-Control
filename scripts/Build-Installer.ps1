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
$icon = Join-Path $branding 'IMRC-Race-Control.ico'
$splash = Join-Path $branding 'IMRC-Race-Control.png'
if (-not (Test-Path $icon)) { throw 'Brand icon was not rendered' }
# Verify the committed application icon matches the Windows-rendered installer icon.
if ((Get-FileHash $icon).Hash -ne (Get-FileHash "src/IMRCRaceControl.App/Assets/RC.ico").Hash) { throw "Application and installer icons differ" }
# Rebuild with the same icon used by RaceControl-Installer.exe and the updater.
Checked @('publish',$project,'-c','Release','-r','win-x64','--self-contained','true','-o','artifacts/publish',"-p:UpdateRepository=$Repository","-p:ApplicationIcon=$icon",'-p:PublishSingleFile=false')
Checked @('tool','run','vpk','--','pack','--packId','IMRCRaceControl.App','--packVersion',$version,'--packDir','artifacts/publish','--mainExe','IMRCRaceControl.exe','--packTitle','IMRC Race Control','--packAuthors','IMRC Racing N Repair','--icon',$icon,'--splashImage',$splash,'--outputDir','artifacts/releases')
$setup = @(Get-ChildItem 'artifacts/releases/*Setup.exe')
if ($setup.Count -ne 1) { throw 'Expected exactly one installer' }
Move-Item $setup[0].FullName 'artifacts/releases/RaceControl-Installer.exe'
# Portable source bundles are not needed by users or by the updater.
Get-ChildItem 'artifacts/releases/*Portable.zip' | Remove-Item
# Internal vpk upload inventory still uses its generated names; gh uploads the final filenames.
Remove-Item 'artifacts/releases/assets.win.json' -ErrorAction SilentlyContinue

Get-ChildItem 'artifacts/releases' -File | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  $([IO.Path]::GetFileName($_.Path))" } | Set-Content 'artifacts/releases/SHA256SUMS.txt'
Write-Host "Installer and update files ready in artifacts/releases (version $version)."
