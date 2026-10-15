@echo off
setlocal enableextensions

rem ============================================================================
rem  Launch the whole demo: backend + 3 dedicated shards, each in its own window.
rem  (No parenthesised ( ) if-blocks - the paths contain '&' and '(x86)'.)
rem ============================================================================

echo About to launch:
echo    - backend      http://localhost:8443
echo    - shard A      Downtown    (27015)
echo    - shard B      Industrial  (27017)
echo    - shard C      Jail        (27019)
echo That's 4 windows (backend + 3 dedicated servers).
echo.
set "CONFIRM="
set /p "CONFIRM=Start everything? (y/N): "
if /I not "%CONFIRM%"=="y" if /I not "%CONFIRM%"=="yes" goto :cancel

echo.
echo Starting backend...
start "Anakin Backend" "%~dp0run-backend.bat"
timeout /t 3 /nobreak >nul

echo Starting shard A...
start "Anakin Shard A" "%~dp0run-shard.bat" A
timeout /t 2 /nobreak >nul

echo Starting shard B...
start "Anakin Shard B" "%~dp0run-shard.bat" B
timeout /t 2 /nobreak >nul

echo Starting shard C...
start "Anakin Shard C" "%~dp0run-shard.bat" C

echo.
echo Launched backend + shards A/B/C in separate windows.
echo Connect a client:  connect local   (or pick a shard from the lobby list)
echo Stop everything:   stop-all.bat
goto :eof

:cancel
echo Cancelled - nothing started.
endlocal
