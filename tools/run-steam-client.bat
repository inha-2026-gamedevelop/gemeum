@echo off
setlocal enabledelayedexpansion

rem Steam 경로로 호스트에 접속한다.
rem
rem 사용법
rem   run-steam-client.bat 76561198000000000   인자로 바로 주기
rem   run-steam-client.bat                      클립보드 또는 직접 입력
rem
rem 호스트 PC의 run-steam-host.bat이 SteamID64를 클립보드에 넣어 준다.

set "ROOT=%~dp0.."
set "HOSTID=%~1"

if not defined HOSTID call :ask_host_id

if not defined HOSTID (
    echo.
    echo   SteamID64를 받지 못했다. 종료한다.
    pause
    exit /b 1
)

rem 오타를 붙잡는다. SteamID64는 7656119로 시작하는 17자리다
echo %HOSTID%| findstr /r /c:"^7656119[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]$" >nul
if errorlevel 1 (
    echo.
    echo   경고: SteamID64 형태가 아니다 - %HOSTID%
    echo   17자리 숫자여야 하고 보통 7656119로 시작한다.
    set /p "GOAHEAD=  그래도 진행할까? (y/N): "
    if /i not "!GOAHEAD!"=="y" exit /b 1
)

call :find_exe
if not defined EXE (
    echo 개발 빌드를 찾을 수 없다.
    echo   %~dp0gemeum.exe
    echo   %ROOT%\Builds\Development\gemeum.exe
    echo Unity에서 [그믐 - 빌드 - 개발용 빌드]로 다시 빌드할 것.
    pause
    exit /b 1
)

for %%I in ("%EXE%") do set "EXEDIR=%%~dpI"

if not exist "%EXEDIR%steam_appid.txt" (
    if exist "%ROOT%\steam_appid.txt" copy /y "%ROOT%\steam_appid.txt" "%EXEDIR%steam_appid.txt" >nul
)

set "LOGDIR=%EXEDIR%logs"
if not exist "%LOGDIR%" mkdir "%LOGDIR%"
set "LOGFILE=%LOGDIR%\steam-client.log"

if exist "%LOGFILE%" del /q "%LOGFILE%"

echo.
echo   %HOSTID% 에 접속한다.

start "" /D "%EXEDIR%" "%EXE%" -steam -client %HOSTID% -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "%LOGFILE%"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0show-steam-client-result.ps1" -LogPath "%LOGFILE%"
if errorlevel 1 echo 로그 요약 스크립트를 실행하지 못했다. show-steam-client-result.ps1이 옆에 있는지 확인할 것.

echo.
echo 전체 로그: %LOGFILE%
echo.
pause
exit /b 0

:ask_host_id
rem 호스트 배치가 클립보드에 넣어 둔 값을 기본값으로 쓴다
for /f "usebackq delims=" %%C in (`powershell -NoProfile -Command "$c = Get-Clipboard; if ($c -match '^\s*(7656119\d{10})\s*$') { $Matches[1] }"`) do set "HOSTID=%%C"

echo.
if defined HOSTID (
    echo   클립보드에서 SteamID64를 찾았다: !HOSTID!
    set /p "INPUT=  엔터를 누르면 이 값으로 접속한다. 다른 값이면 붙여넣을 것: "
) else (
    echo   클립보드에 SteamID64가 없다.
    set /p "INPUT=  호스트의 SteamID64를 붙여넣을 것: "
)

if not "!INPUT!"=="" set "HOSTID=!INPUT!"
exit /b 0

:find_exe
set "EXE="
if exist "%~dp0gemeum.exe" set "EXE=%~dp0gemeum.exe"
if not defined EXE if exist "%ROOT%\Builds\Development\gemeum.exe" set "EXE=%ROOT%\Builds\Development\gemeum.exe"
exit /b 0
