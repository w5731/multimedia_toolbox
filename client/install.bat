@echo off
rem ============================================================
rem  MultimediaClient one-click install (Windows 7 SP1+)
rem  - checks .NET Framework 4.8
rem  - starts the client; on first run a pairing wizard appears
rem  - the client registers itself to auto-start on logon
rem ============================================================

setlocal
set EXE=%~dp0MultimediaClient.exe

if not exist "%EXE%" (
  echo [ERROR] MultimediaClient.exe not found next to this script.
  pause
  exit /b 1
)

rem --- check .NET Framework v4 ---
set REL=0
for /f "tokens=3" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release 2^>nul ^| findstr /i "Release"') do set REL=%%a
set /a RELD=%REL% 2>nul
if "%REL%"=="0" (
  echo [ERROR] .NET Framework 4.x not found.
  echo Please install ".NET Framework 4.8" first, then rerun this script.
  echo Download: https://dotnet.microsoft.com/download/dotnet-framework/net48
  pause
  exit /b 1
)
if %RELD% LSS 528040 (
  echo [WARN] .NET Framework 4.8 or newer is recommended ^(current Release=%RELD%^).
  echo The program may still run. If it fails, install .NET Framework 4.8.
)

rem --- start client ---
start "" "%EXE%"
echo.
echo Started. On FIRST run a pairing window appears:
echo   1. open the teacher website on the server
echo   2. Clients page -^> create/select class -^> generate pairing code
echo   3. enter server URL ^(e.g. http://SERVER-IP:19283^) and the 6-digit code
echo The client auto-starts at every logon from now on.
echo To exit or change settings: tray icon ^(teacher password, default 123456^).
endlocal
