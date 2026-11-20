@echo off
rem Fermata for Windows - the only file you need. Double-click it.
rem  - The first time it fetches Fermata.exe (no installation, no administrator rights).
rem  - It keeps the program in C:\Users\<name>\Program\Fermata, the folder that locked
rem    (managed) computers usually allow programs to run from.
rem  - Later it starts at once, and fetches a newer version only when there is one.
rem  - Fermata then opens the Fermata page in the web browser.
setlocal
set "DEST=%USERPROFILE%\Program\Fermata"
set "EXE=%DEST%\Fermata.exe"
set "URL=https://github.com/ibst1/apps/releases/download/fermata-windows/Fermata.exe"
if not exist "%DEST%" mkdir "%DEST%"

rem Already running: just open the page again
tasklist /FI "IMAGENAME eq Fermata.exe" 2>nul | find /I "Fermata.exe" >nul && goto start

rem A Fermata.exe next to this file (e.g. from a zip) is used as it is
if exist "%~dp0Fermata.exe" if /I not "%~dp0"=="%DEST%\" copy /Y "%~dp0Fermata.exe" "%EXE%" >nul

rem Fetch (or update) - only downloads when the file on the server is newer
if exist "%EXE%" (
  curl.exe -L -s --fail --connect-timeout 5 -z "%EXE%" -o "%EXE%.part" "%URL%" && if exist "%EXE%.part" move /Y "%EXE%.part" "%EXE%" >nul
) else (
  echo Hamtar Fermata ... ^(cirka 70 MB, en gang^)
  curl.exe -L --fail --progress-bar --connect-timeout 10 -o "%EXE%.part" "%URL%" && move /Y "%EXE%.part" "%EXE%" >nul
)
if exist "%EXE%.part" del "%EXE%.part" >nul 2>&1
if not exist "%EXE%" (
  echo.
  echo Kunde inte hamta Fermata. Kontrollera internetanslutningen och forsok igen.
  pause
  exit /b 1
)

rem Keep a copy of this start file next to the program
if /I not "%~dp0"=="%DEST%\" copy /Y "%~f0" "%DEST%\Starta Fermata.cmd" >nul 2>&1

:start
start "" "%EXE%" %*
endlocal
