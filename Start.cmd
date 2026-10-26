@echo off
if not exist "%~dp0app\FairyLauncher.exe" (
  echo Run Install.cmd first.
  pause
  exit /b 1
)
start "" "%~dp0app\FairyLauncher.exe"
