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

powershell -NoProfile -ExecutionPolicy Bypass -File ".\build.ps1"
set EXITCODE=%ERRORLEVEL%

echo.
echo ------------------------------------------------------------
if %EXITCODE% EQU 0 (
  echo Publish completed successfully.
  echo EXE folder: %CD%\artifacts\publish\win-x64
) else (
  echo PUBLISH FAILED with exit code %EXITCODE%.
  echo Review the errors above. No successful publish was produced by this run.
)
echo.
echo This window will remain open so all warnings/errors can be reviewed.
echo ------------------------------------------------------------
echo.
pause
exit /b %EXITCODE%
