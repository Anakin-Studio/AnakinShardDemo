@echo off
setlocal enableextensions

rem ============================================================================
rem  Stop all shards + the backend started by run-all.bat.
rem ============================================================================

echo Stopping shards (sbox-server.exe)...
taskkill /F /IM sbox-server.exe >nul 2>&1 && echo   shards stopped || echo   no shards were running

echo Stopping backend (dotnet hosting AnakinShardBackend)...
powershell -NoProfile -Command "Get-CimInstance Win32_Process | ? { $_.Name -eq 'dotnet.exe' -and $_.CommandLine -like '*AnakinShardBackend*' } | % { Stop-Process $_.ProcessId -Force }" 2>nul
echo   backend stopped (if it was running)

echo Done.
endlocal
