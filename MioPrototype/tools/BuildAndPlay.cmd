@echo off
rem ===================================================================
rem  Launch Build\MioPrototype.exe with the latest code.
rem
rem  - Unity is closed: batch-build only if sources are newer than the build
rem  - Unity is open: cannot batch-build; just launch (the editor's
rem    "Ryona > Auto Build Player" keeps the build fresh)
rem
rem  Double-click this file, or run it from a shell.
rem ===================================================================
setlocal
set "PROJECT=%~dp0.."
pushd "%PROJECT%" || exit /b 1

set "EXE=%PROJECT%\Build\MioPrototype.exe"

rem Unity version from ProjectSettings\ProjectVersion.txt
set "VERSION="
for /f "tokens=2" %%v in (ProjectSettings\ProjectVersion.txt) do set "VERSION=%%v"
if "%VERSION%"=="" goto :launch

set "UNITY="
if exist "%ProgramFiles%\Unity\Hub\Editor\%VERSION%\Editor\Unity.exe" set "UNITY=%ProgramFiles%\Unity\Hub\Editor\%VERSION%\Editor\Unity.exe"
if not defined UNITY if exist "%USERPROFILE%\Unity\Editor\%VERSION%\Editor\Unity.exe" set "UNITY=%USERPROFILE%\Unity\Editor\%VERSION%\Editor\Unity.exe"
if not defined UNITY goto :launch

tasklist /fi "imagename eq Unity.exe" 2>nul | "%SystemRoot%\System32\find.exe" /i "Unity.exe" >nul
if not errorlevel 1 (
  echo [build] Unity is running - launching the current build.
  goto :launch
)

echo [build] rebuilding if anything changed...
"%UNITY%" -batchmode -nographics -quit -projectPath "%PROJECT%" -executeMethod Ryona.EditorTools.PlayerBuild.BuildWindowsFromCli -logFile "%PROJECT%\Logs\batchbuild.log"
if errorlevel 1 echo [build] build failed - see Logs\batchbuild.log

:launch
if not exist "%EXE%" (
  echo [build] missing %EXE%
  popd
  exit /b 1
)
echo [build] launching %EXE%
start "" "%EXE%"
popd
