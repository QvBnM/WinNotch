@echo off
cd /d "%~dp0"
title WinNotch - teste
set "DOTNET=dotnet"
dotnet --list-sdks 2>nul | findstr /r "^[0-9]" >nul
if errorlevel 1 if exist "%LOCALAPPDATA%\WinNotch\dotnet\dotnet.exe" set "DOTNET=%LOCALAPPDATA%\WinNotch\dotnet\dotnet.exe"
echo Teste aplicatie (server extensie, adrese, calendar, viteza, calculator)...
"%DOTNET%" run --project WinNotch.Tests.csproj -c Release
set APP=%errorlevel%
where node >nul 2>nul
if errorlevel 1 (
  echo Node.js nu e instalat: sar peste testele extensiei.
  set EXT=0
) else (
  echo.
  echo Teste extensie...
  node extension\ext.test.js
  node extension\content.test.js
)
pause
