@echo off
setlocal
if not exist "%~dp0CodexWeekly.exe" (
  echo CodexWeekly.exe is missing. Run Build.ps1 first.
  pause
  exit /b 1
)
start "" "%~dp0CodexWeekly.exe"
