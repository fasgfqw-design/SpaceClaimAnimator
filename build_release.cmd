@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "SCAPI=%~1"
if not "%SCAPI%"=="" goto api_check

if defined AWP_ROOT261 (
  if exist "%AWP_ROOT261%\scdm\SpaceClaim.Api.V261\SpaceClaim.Api.V261.dll" set "SCAPI=%AWP_ROOT261%\scdm\SpaceClaim.Api.V261"
  if not defined SCAPI if exist "%AWP_ROOT261%\scdm\SpaceClaim.Api.V261\bin\x64\Release\SpaceClaim.Api.V261.dll" set "SCAPI=%AWP_ROOT261%\scdm\SpaceClaim.Api.V261\bin\x64\Release"
)

if not defined SCAPI if exist "C:\Program Files\ANSYS Inc\v261\scdm\SpaceClaim.Api.V261\SpaceClaim.Api.V261.dll" set "SCAPI=C:\Program Files\ANSYS Inc\v261\scdm\SpaceClaim.Api.V261"
if not defined SCAPI if exist "C:\Program Files\ANSYS Inc\v261\scdm\SpaceClaim.Api.V261\bin\x64\Release\SpaceClaim.Api.V261.dll" set "SCAPI=C:\Program Files\ANSYS Inc\v261\scdm\SpaceClaim.Api.V261\bin\x64\Release"

:api_check
if not exist "%SCAPI%\SpaceClaim.Api.V261.dll" (
  echo.
  echo ERROR: SpaceClaim.Api.V261.dll was not found.
  echo Example:
  echo build_release.cmd "F:\Program Files\Ansys Inc\v261\scdm\SpaceClaim.Api.V261"
  echo.
  exit /b 2
)

set "MSBUILD="
where msbuild.exe >nul 2>nul && set "MSBUILD=msbuild.exe"

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not defined MSBUILD (
  if exist "%VSWHERE%" (
    for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"
  )
)

set "TARGET_OVERRIDE="
if defined MSBUILD (
  for %%i in ("%MSBUILD%") do if not exist "%%~dpiRoslyn\Microsoft.CSharp.Core.targets" set "MSBUILD="
)
if not defined MSBUILD if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" (
  set "MSBUILD=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
  set "TARGET_OVERRIDE=/p:TargetFrameworkVersion=v4.8"
)

if not defined MSBUILD (
  echo ERROR: MSBuild was not found. Install Visual Studio Build Tools with .NET desktop build tools.
  exit /b 3
)

echo API: %SCAPI%
echo MSBuild: %MSBUILD%
"%MSBUILD%" SCAnimator.V261.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64 /p:SpaceClaimApiDir="%SCAPI%" %TARGET_OVERRIDE% /m
if errorlevel 1 exit /b %errorlevel%

echo.
echo Build complete:
echo %~dp0bin\x64\Release\SCAnimator.V261.dll
exit /b 0
