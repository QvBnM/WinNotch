@echo off
cd /d "%~dp0"
rem Starts the built app; builds it first if it isn't there yet.
if exist "publish\WinNotch.exe" (
  start "" "%~dp0publish\WinNotch.exe"
  exit /b 0
)
call "%~dp0build.bat"
