@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "SOURCE=%~dp0bin\x64\Release"
set "TARGET=%ProgramData%\SpaceClaim\AddIns\SCAnimator\V261"

if not exist "%SOURCE%\SCAnimator.V261.dll" (
  echo ERROR: Release DLL not found. Run build_release.cmd first.
  exit /b 2
)
if not exist "%SOURCE%\SCAnimator.V261.Manifest.xml" (
  echo ERROR: Release manifest not found. Run build_release.cmd first.
  exit /b 2
)

if not exist "%TARGET%" mkdir "%TARGET%"
if errorlevel 1 (
  echo ERROR: Cannot create %TARGET%
  echo Try running this script as Administrator.
  exit /b 3
)

copy /Y "%SOURCE%\SCAnimator.V261.dll" "%TARGET%\SCAnimator.V261.dll" >nul
if errorlevel 1 goto copy_failed
copy /Y "%SOURCE%\SCAnimator.V261.Manifest.xml" "%TARGET%\SCAnimator.V261.Manifest.xml" >nul
if errorlevel 1 goto copy_failed
if exist "%SOURCE%\SCAnimator.V261.pdb" copy /Y "%SOURCE%\SCAnimator.V261.pdb" "%TARGET%\SCAnimator.V261.pdb" >nul

echo Installed to:
echo %TARGET%
echo.
echo Restart SpaceClaim completely.
exit /b 0

:copy_failed
echo ERROR: Copy failed. Close SpaceClaim and check destination permissions.
exit /b 4
