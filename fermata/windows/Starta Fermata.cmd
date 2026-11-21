@echo off
rem Fermata for Windows - the only file you need. Double-click it.
rem  - Starts Fermata AT ONCE if it is already on this computer.
rem  - A newer version is fetched in the background and checked in advance (a hidden test start,
rem    so Windows' security scan is done before anyone waits). It is used the next time.
rem  - Only the very first time on a computer does it wait for the download.
rem  - The program lives in C:\Users\<name>\Program\Fermata, the folder that locked (managed)
rem    computers usually allow programs to run from. No installation, no administrator rights.
rem Downloads go through PowerShell (uses the computer's proxy settings like the browser;
rem curl.exe is blocked on some managed computers).
setlocal
set "DEST=%USERPROFILE%\Program\Fermata"
if defined FERMATA_DEST set "DEST=%FERMATA_DEST%"
set "FEXE=%DEST%\Fermata.exe"
set "FNEW=%DEST%\Fermata.new.exe"
set "FURL=https://github.com/ibst1/apps/releases/download/fermata-windows/Fermata.exe"
if not exist "%DEST%" mkdir "%DEST%"

rem Already running: just open the page again
if not defined FERMATA_DEST tasklist /FI "IMAGENAME eq Fermata.exe" 2>nul | find /I "Fermata.exe" >nul && goto start

rem A version fetched (and checked) in the background last time: use it now
if exist "%FNEW%" if exist "%FNEW%.ok" (
  move /Y "%FNEW%" "%FEXE%" >nul && del "%FNEW%.ok" >nul 2>&1
  if exist "%FNEW%.etag" move /Y "%FNEW%.etag" "%FEXE%.etag" >nul
)

rem A Fermata.exe next to this file (e.g. from a zip) is used as it is
if exist "%~dp0Fermata.exe" if /I not "%~dp0"=="%DEST%\" copy /Y "%~dp0Fermata.exe" "%FEXE%" >nul

if exist "%FEXE%" goto haveexe
echo Hamtar Fermata forsta gangen ... (cirka 70 MB, gors bara en gang)
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference='SilentlyContinue'; $exe=$env:FEXE; $url=$env:FURL; try { $r=Invoke-WebRequest $url -OutFile ($exe+'.part') -UseBasicParsing -PassThru; Move-Item ($exe+'.part') $exe -Force; if ($r.Headers['ETag']) { Set-Content ($exe+'.etag') $r.Headers['ETag'] } } catch { Remove-Item ($exe+'.part') -ErrorAction SilentlyContinue }"
if not exist "%FEXE%" (
  echo.
  echo Kunde inte hamta Fermata. Kontrollera internetanslutningen och forsok igen.
  pause
  exit /b 1
)
echo Startar Fermata ... (forsta starten kan ta en stund medan Windows kontrollerar programmet)

:haveexe
rem Keep a copy of this start file next to the program
if /I not "%~dp0"=="%DEST%\" copy /Y "%~f0" "%DEST%\Starta Fermata.cmd" >nul 2>&1

rem Background: fetch a newer version (if any) and pre-check it with a hidden test start
start "" /MIN powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command "$ProgressPreference='SilentlyContinue'; $exe=$env:FEXE; $new=$env:FNEW; $url=$env:FURL; try { $et=(Invoke-WebRequest $url -Method Head -UseBasicParsing -TimeoutSec 15).Headers['ETag']; $cur = if (Test-Path ($exe+'.etag')) { Get-Content ($exe+'.etag') } else { '' }; if ($et -and $et -ne $cur -and -not (Test-Path ($new+'.ok'))) { Invoke-WebRequest $url -OutFile ($new+'.part') -UseBasicParsing; Move-Item ($new+'.part') $new -Force; Set-Content ($new+'.etag') $et; Start-Process $new -ArgumentList '--check' -WindowStyle Hidden -Wait; Set-Content ($new+'.ok') 'ok' } } catch { Remove-Item ($new+'.part') -ErrorAction SilentlyContinue }"

:start
start "" "%FEXE%" %*
endlocal
