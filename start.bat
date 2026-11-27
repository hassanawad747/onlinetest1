@echo off
setlocal
cd /d "%~dp0"
if /I "%~1"=="server" goto server

set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

call :ensurehost
call :listening
if not errorlevel 1 goto open

start "OnlineTest" /D "%~dp0" cmd /k call "%~f0" server

set /a tries=0
:wait
set /a tries+=1
if %tries% GTR 45 goto failed
call :listening
if not errorlevel 1 goto open
ping -n 2 127.0.0.1 >nul
goto wait

:failed
echo OnlineTest did not start. The OnlineTest window shows the error.
pause
exit /b 1

:open
start "" "http://onlinetest.local:5088/"
exit /b 0

:ensurehost
findstr /I /C:"onlinetest.local" "%SystemRoot%\System32\drivers\etc\hosts" >nul 2>&1
if not errorlevel 1 goto :eof
powershell -NoProfile -Command "Start-Process powershell.exe -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File %~dp0ensure-host.ps1'"
goto :eof

:listening
powershell -NoProfile -Command "try { $c = New-Object System.Net.Sockets.TcpClient; $c.Connect('127.0.0.1',5088); $c.Close(); exit 0 } catch { exit 1 }"
goto :eof

:server
cd /d "%~dp0"
set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"
:run
echo Starting OnlineTest at http://127.0.0.1:5088
"%DOTNET%" run --urls http://127.0.0.1:5088
echo OnlineTest stopped. Starting again in 3 seconds.
ping -n 4 127.0.0.1 >nul
goto run
