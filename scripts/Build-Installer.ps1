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
Checked @('tool','run','vpk','--','pack','--packId','IMRCRaceControl','--packVersion',$version,'--packDir','artifacts/publish','--mainExe','IMRCRaceControl.exe','--packTitle','IMRC Race Control','--packAuthors','IMRC Racing N Repair','--outputDir','artifacts/releases')
if (-not (Get-ChildItem 'artifacts/releases/*Setup.exe')) { throw 'Installer was not generated' }
Get-ChildItem 'artifacts/releases' -File | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  $([IO.Path]::GetFileName($_.Path))" } | Set-Content 'artifacts/releases/SHA256SUMS.txt'
Write-Host "Installer and update files ready in artifacts/releases (version $version)."
