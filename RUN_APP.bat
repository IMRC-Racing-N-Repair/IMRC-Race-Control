@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET SDK was not found.
  echo Install the .NET 10 SDK / Visual Studio .NET Desktop Development workload first.
  echo.
  pause
  exit /b 1
)

echo Starting IMRC Race Control...
echo Crash logs: %%LOCALAPPDATA%%\IMRCRaceControl\Logs
echo.

dotnet run --project ".\src\IMRCRaceControl.App\IMRCRaceControl.App.csproj"
set EXITCODE=%ERRORLEVEL%

echo.
echo ------------------------------------------------------------
echo IMRC Race Control exited with code %EXITCODE%.
echo Crash/runtime logs are stored at:
echo %LOCALAPPDATA%\IMRCRaceControl\Logs
echo ------------------------------------------------------------
echo.
pause
exit /b %EXITCODE%
