@echo off
if not exist "%~dp0.venv\Scripts\python.exe" (
  echo Run Install.cmd first.
  pause
  exit /b 1
)
"%~dp0.venv\Scripts\python.exe" -X utf8 "%~dp0tools\configure.py"
if errorlevel 1 pause
