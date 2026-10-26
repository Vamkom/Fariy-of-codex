@echo off
if not exist "%~dp0.venv\Scripts\python.exe" (
  echo Run Install.cmd first.
  pause
  exit /b 1
)
if "%~1"=="" (
  echo Drag your PRIVATE asset zip onto this file.
  pause
  exit /b 1
)
"%~dp0.venv\Scripts\python.exe" -X utf8 "%~dp0tools\assets.py" import "%~1"
if errorlevel 1 (
  pause
  exit /b 1
)
echo Imported. Close this installation and run Install.cmd again to rebuild its executable icon.
pause
