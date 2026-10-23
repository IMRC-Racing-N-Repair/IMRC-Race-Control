$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
[xml]$project = Get-Content src/IMRCRaceControl.App/IMRCRaceControl.App.csproj
$version = [string]$project.Project.PropertyGroup.Version
$root = Join-Path (Get-Location) 'artifacts/installer-smoke/App'
$baseline = Join-Path (Get-Location) 'artifacts/installer-smoke/Previous-Installer.exe'
New-Item (Split-Path $baseline) -ItemType Directory -Force | Out-Null
function Run-Setup([string]$Exe, [string]$ExpectedVersion) {
    $process = Start-Process -FilePath $Exe -ArgumentList @('--silent','--installto',"`"$root`"") -PassThru
    if (-not $process.WaitForExit(180000)) { throw 'Installer did not complete unattended; a stock prompt may be blocking it.' }
    if ($process.ExitCode -ne 0) { throw "Installation failed with exit code $($process.ExitCode)" }
    $exePath = Join-Path $root 'current/IMRCRaceControl.exe'
    if (-not (Test-Path $exePath)) { throw 'Installed application is missing' }
    $actual = [Version](Get-Item $exePath).VersionInfo.FileVersion
    if ($actual.ToString(3) -ne $ExpectedVersion) { throw "Expected $ExpectedVersion, installed $actual" }
}
$installer = Join-Path (Get-Location) 'artifacts/releases/RaceControl-Installer.exe'
# Exercise the embedded engine through the same public EXE users download.
Run-Setup $installer $version
Write-Host 'Fresh installation passed.'
Run-Setup $installer $version
Write-Host 'Same-version repair passed.'
$appGate = [Threading.Mutex]::new($false, ('Local\IMRC.RaceControl.' + [Environment]::UserName))
try {
    $blocked = Start-Process -FilePath $installer -ArgumentList @('--silent','--installto',"`"$root`"") -PassThru
    if (-not $blocked.WaitForExit(30000)) { throw 'Installer did not reject an active Race Control session' }
    if ($blocked.ExitCode -eq 0) { throw 'Installer must not replace files while Race Control is active' }
} finally { $appGate.Dispose() }
Write-Host 'Active Race Control session protection passed.' 
# Keep the update-test baseline on the retained public release. Earlier releases
# can be removed without breaking future installer builds.
$baselineVersion = '0.16.23'
if ([Version]$version -gt [Version]$baselineVersion) {
    $root = Join-Path (Get-Location) 'artifacts/installer-smoke/UpgradeApp'
    Invoke-WebRequest -Uri "https://github.com/IMRC-Racing-N-Repair/IMRC-Race-Control/releases/download/v$baselineVersion/RaceControl-Installer.exe" -OutFile $baseline
    Run-Setup $baseline $baselineVersion
    Run-Setup $installer $version
    Write-Host "Upgrade from $baselineVersion passed."
} else {
    Write-Host "Update baseline is $baselineVersion; the older-version upgrade check runs when building a newer version."
}
