@echo off
rem ===================================================================
rem  Haul demo launcher (full paths)
rem
rem  仰向け -> 背後拘束 / うつ伏せ -> 前方拘束 を交互に再生する。
rem  Esc で終了。
rem ===================================================================
setlocal

set "EXE=C:\gemini-desktop\ryona-action\MioPrototype\Build\MioPrototype.exe"
set "DIR=C:\gemini-desktop\ryona-action\MioPrototype\Build"

if not exist "%EXE%" (
  echo [error] not found: %EXE%
  echo Build the player first.
  pause
  exit /b 1
)

start "" /d "%DIR%" "%EXE%" -hauldemo
exit /b 0
