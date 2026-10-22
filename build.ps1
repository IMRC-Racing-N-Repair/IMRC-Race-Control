# IMRC Race Control v0.15.0 Windows x64 publish script
$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "src\IMRCRaceControl.App\IMRCRaceControl.App.csproj"
$out = Join-Path $PSScriptRoot "artifacts\publish\win-x64"

function Invoke-DotNetChecked {
    param(
        [Parameter(Mandatory=$true)]
        [string[]] $Arguments
    )

    & dotnet @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

try {
    Write-Host "Restoring packages..."
    Invoke-DotNetChecked @("restore", $project)

    Write-Host "Publishing self-contained Windows x64 application..."
    Invoke-DotNetChecked @(
        "publish",
        $project,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-o", $out,
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true"
    )

    Write-Host ""
    Write-Host "Publish succeeded:" -ForegroundColor Green
    Write-Host $out
    Write-Host ""
    Write-Host "Look for IMRCRaceControl.exe in that folder."
    exit 0
}
catch {
    Write-Host ""
    Write-Host "PUBLISH FAILED" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    Write-Host "No successful publish should be assumed from this run."
    exit 1
}
