@echo off
setlocal enabledelayedexpansion

rem Steam 경로로 호스트를 연다. PC 2대, Steam 계정 2개가 필요하다.
rem KCP 경로는 run-host.bat 을 쓴다.
rem
rem 준비물
rem   1. Steam 클라이언트가 실행 중이고 로그인되어 있어야 한다
rem   2. 개발 빌드여야 한다. NetworkLaunchArgs가 DEVELOPMENT_BUILD에서만 동작한다

set "ROOT=%~dp0.."

rem 런을 시작할 인원. 안 주면 씬에 저장된 값을 쓴다
set "PLAYERS=%~1"
set "PLAYERARGS="
if defined PLAYERS set "PLAYERARGS=-minplayers %PLAYERS%"

call :find_exe
if not defined EXE (
    echo 개발 빌드를 찾을 수 없다.
    echo   %~dp0gemeum.exe
    echo   %ROOT%\Builds\Development\gemeum.exe
    echo Unity에서 [그믐 - 빌드 - 개발용 빌드]로 다시 빌드할 것.
    echo 릴리스 빌드로는 실행 인자가 동작하지 않는다.
    pause
    exit /b 1
)

for %%I in ("%EXE%") do set "EXEDIR=%%~dpI"

rem SteamAPI.Init은 exe 옆의 steam_appid.txt를 읽는다. 빌드가 챙기지만 없으면 여기서 보충한다
if not exist "%EXEDIR%steam_appid.txt" (
    if exist "%ROOT%\steam_appid.txt" (
        copy /y "%ROOT%\steam_appid.txt" "%EXEDIR%steam_appid.txt" >nul
        echo steam_appid.txt를 빌드 폴더에 복사했다.
    ) else (
        echo 경고: steam_appid.txt가 없다. Steam 초기화가 실패할 수 있다.
    )
)

set "LOGDIR=%EXEDIR%logs"
if not exist "%LOGDIR%" mkdir "%LOGDIR%"
set "LOGFILE=%LOGDIR%\steam-host.log"

rem 이전 로그가 남아 있으면 이번 실행 결과와 섞인다
if exist "%LOGFILE%" del /q "%LOGFILE%"

echo.
echo   Steam 호스트를 시작한다.
echo   로그의 SteamID64를 다른 PC에 알려줄 것.

start "" /D "%EXEDIR%" "%EXE%" -steam -host %PLAYERARGS% -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "%LOGFILE%"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0show-steam-host-id.ps1" -LogPath "%LOGFILE%"
if errorlevel 1 echo 로그 요약 스크립트를 실행하지 못했다. show-steam-host-id.ps1이 옆에 있는지 확인할 것.
echo.
echo 전체 로그: %LOGFILE%
echo.
pause
exit /b 0

:find_exe
rem 빌드 폴더에 함께 복사된 경우를 먼저 본다. 그다음이 tools에서 실행한 경우다
set "EXE="
if exist "%~dp0gemeum.exe" set "EXE=%~dp0gemeum.exe"
if not defined EXE if exist "%ROOT%\Builds\Development\gemeum.exe" set "EXE=%ROOT%\Builds\Development\gemeum.exe"
exit /b 0
