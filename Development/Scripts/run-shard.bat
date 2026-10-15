@echo off
setlocal enableextensions

rem ============================================================================
rem  Launch one AnakinShardDemo dedicated server (shard A / B / C).
rem  Usage:  run-shard.bat A        (defaults to A if omitted)
rem
rem  NOTE: no parenthesised ( ) if-blocks below on purpose - the server path
rem  contains "(x86)" and cmd's block parser breaks on the ')' inside it.
rem ============================================================================

set "SHARD=%~1"
if "%SHARD%"=="" set "SHARD=A"

rem --- sbox-server.exe ships WITH s&box. Override with the SBOX_SERVER env var. ---
if not defined SBOX_SERVER set "SBOX_SERVER=C:\Program Files (x86)\Steam\steamapps\common\sbox\sbox-server.exe"
if not exist "%SBOX_SERVER%" if exist "C:\Program Files (x86)\Steam\steamapps\common\sbox 2129370\sbox-server.exe" set "SBOX_SERVER=C:\Program Files (x86)\Steam\steamapps\common\sbox 2129370\sbox-server.exe"

rem --- Project file (this repo). %~dp0 is the scripts\ folder. ---
set "PROJECT=%~dp0..\anakinsharddemo.sbproj"

set "NAME="
set "PORT="
set "QPORT="
if /I "%SHARD%"=="A" set "NAME=Anakin Shard A - Downtown"
if /I "%SHARD%"=="A" set "PORT=27015"
if /I "%SHARD%"=="A" set "QPORT=27016"
if /I "%SHARD%"=="B" set "NAME=Anakin Shard B - Industrial"
if /I "%SHARD%"=="B" set "PORT=27017"
if /I "%SHARD%"=="B" set "QPORT=27018"
if /I "%SHARD%"=="C" set "NAME=Anakin Shard C - Jail"
if /I "%SHARD%"=="C" set "PORT=27019"
if /I "%SHARD%"=="C" set "QPORT=27020"

if not defined PORT goto :badshard
if not exist "%SBOX_SERVER%" goto :noserver

echo Starting shard %SHARD%  "%NAME%"  port %PORT% / query %QPORT%
echo   server:  "%SBOX_SERVER%"
echo   project: "%PROJECT%"
echo   backend: http://localhost:8443
echo.
"%SBOX_SERVER%" +game "%PROJECT%" +hostname "%NAME%" +port %PORT% +net_query_port %QPORT% +net_allow_local 1 +anakin_shard %SHARD% -allowlocalhttp
goto :eof

:badshard
echo Unknown shard "%SHARD%" - use A, B or C.
exit /b 1

:noserver
echo.
echo   sbox-server.exe not found at:
echo     %SBOX_SERVER%
echo   Set the SBOX_SERVER env var or edit this script.
exit /b 1
