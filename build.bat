@echo off
setlocal
cd /d "%~dp0"
title WinNotch - build

rem --- Find a .NET SDK, in this order:
rem     1. installed in Windows   2. the shared copy for your account (%LOCALAPPDATA%\WinNotch\dotnet),
rem     downloaded once and used by every WinNotch version you unzip   3. an older copy next to this file (.dotnet)
set "SDKDIR=%LOCALAPPDATA%\WinNotch\dotnet"
set "DOTNET=dotnet"
dotnet --list-sdks 2>nul | findstr /r "^[0-9]" >nul
if not errorlevel 1 goto :have_sdk
set "DOTNET=%SDKDIR%\dotnet.exe"
if exist "%DOTNET%" "%DOTNET%" --list-sdks 2>nul | findstr /r "^[0-9]" >nul
if exist "%DOTNET%" if not errorlevel 1 goto :have_sdk
if exist "%~dp0.dotnet\dotnet.exe" (
  echo Mut SDK-ul descarcat anterior intr-un loc comun, ca sa nu-l mai descarci la fiecare versiune...
  if not exist "%LOCALAPPDATA%\WinNotch" mkdir "%LOCALAPPDATA%\WinNotch"
  robocopy "%~dp0.dotnet" "%SDKDIR%" /E /MOVE /NFL /NDL /NJH /NJS /NP >nul
)
if exist "%DOTNET%" "%DOTNET%" --list-sdks 2>nul | findstr /r "^[0-9]" >nul
if exist "%DOTNET%" if not errorlevel 1 goto :have_sdk

echo.
echo Pe acest calculator nu este instalat .NET SDK (e nevoie de el doar pentru build).
echo Exista doar runtime-ul, care ruleaza aplicatii dar nu le poate construi.
echo.
echo Pot sa-l descarc acum de la Microsoft (dot.net), O SINGURA DATA pentru contul tau,
echo in %SDKDIR%
echo Il vor folosi toate versiunile WinNotch pe care le dezarhivezi de acum inainte.
echo Fara instalare in Windows si fara drepturi de administrator. Sunt cam 250 MB.
echo.
choice /C DN /M "Descarc SDK-ul acum? D = da, N = nu"
if errorlevel 2 goto :manual_sdk

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\get-sdk.ps1" -Dest "%SDKDIR%"
if not exist "%SDKDIR%\dotnet.exe" (
  echo.
  echo Incerc a doua metoda, scriptul oficial Microsoft - afiseaza pasii pe rand...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; $s=Join-Path $env:TEMP 'dotnet-install.ps1'; Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $s -UseBasicParsing; if ((Get-AuthenticodeSignature $s).Status -ne 'Valid') { throw 'scriptul Microsoft nu are semnatura valida' }; & $s -Channel 8.0 -InstallDir '%SDKDIR%' -NoPath -Verbose"
)
if not exist "%SDKDIR%\dotnet.exe" goto :manual_sdk
set "DOTNET=%SDKDIR%\dotnet.exe"
"%DOTNET%" --list-sdks 2>nul | findstr /r "^[0-9]" >nul
if errorlevel 1 goto :manual_sdk
echo.
echo SDK descarcat.
goto :have_sdk

:manual_sdk
echo.
echo Instaleaza manual .NET 8 SDK (o singura data):
echo   https://dotnet.microsoft.com/download/dotnet/8.0   -  coloana "SDK", "Windows x64"
echo Dupa instalare inchide fereastra aceasta si porneste din nou build.bat.
start "" "https://dotnet.microsoft.com/download/dotnet/8.0"
pause
exit /b 1

:have_sdk
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"

rem --- Close the WinNotch that is running now, so the exe can be replaced (not the temperature helper: SYSTEM, session 0, Program Files) ---
tasklist /FI "IMAGENAME eq WinNotch.exe" /FI "SESSION ne 0" 2>nul | find /I "WinNotch.exe" >nul
if errorlevel 1 goto :build
echo Inchid WinNotch care ruleaza acum, ca sa pot pune versiunea noua...
taskkill /IM WinNotch.exe /FI "SESSION ne 0" /F >nul 2>nul
timeout /t 1 /nobreak >nul
tasklist /FI "IMAGENAME eq WinNotch.exe" /FI "SESSION ne 0" 2>nul | find /I "WinNotch.exe" >nul
if errorlevel 1 goto :build
echo WinNotch ruleaza ca administrator. Apasa "Da" in fereastra Windows ca sa-l inchid.
powershell -NoProfile -Command "Start-Process taskkill -ArgumentList '/IM WinNotch.exe /FI \"SESSION ne 0\" /F' -Verb RunAs -WindowStyle Hidden -Wait"
timeout /t 1 /nobreak >nul
tasklist /FI "IMAGENAME eq WinNotch.exe" /FI "SESSION ne 0" 2>nul | find /I "WinNotch.exe" >nul
if errorlevel 1 goto :build
echo Nu am putut inchide WinNotch. Inchide-l din iconita de langa ceas - Iesire, apoi apasa o tasta.
pause

:build
echo.
echo Construiesc WinNotch.exe (prima data dureaza 1-2 minute, descarca pachetele)...
echo.
if exist "publish\WinNotch.exe" del /q "publish\WinNotch.exe" >nul 2>nul
"%DOTNET%" publish WinNotch.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
rem dotnet can exit with a negative code, which "if errorlevel 1" doesn't catch: check for zero exactly, and for the file.
if not "%errorlevel%"=="0" goto :failed
if not exist "publish\WinNotch.exe" goto :failed

echo.
echo Gata!  Aplicatia este in:  %~dp0publish\WinNotch.exe
echo Se porneste acum. O gasesti si in zona de notificari, langa ceas.
start "" "%~dp0publish\WinNotch.exe"
pause
exit /b 0

:failed
echo.
echo ============================================================
echo  Build-ul a esuat. Copiaza tot textul de mai sus si trimite-l
echo  in conversatie, ca sa repar eroarea.
echo ============================================================
pause
exit /b 1
