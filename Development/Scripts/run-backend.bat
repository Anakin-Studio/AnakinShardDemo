@echo off
setlocal enableextensions

rem ============================================================================
rem  Start the AnakinShardDemo backend on http://localhost:8443
rem  (8443, not 8080 - 8080 is taken by NVIDIA Broadcast on this machine, and
rem   8443 is on s&box's localhost HTTP allowlist so the editor can reach it too)
rem ============================================================================

set "PROJ=%~dp0..\backend\AnakinShardBackend.csproj"
set "DLL=%~dp0..\backend\bin\Debug\net10.0\AnakinShardBackend.dll"

if not exist "%DLL%" (
  echo Building backend...
  dotnet build "%PROJ%" -v q -nologo
  if errorlevel 1 ( echo Build failed. & exit /b 1 )
)

echo Starting backend on http://localhost:8443 ...
pushd "%~dp0..\backend"
dotnet "%DLL%"
popd

endlocal
