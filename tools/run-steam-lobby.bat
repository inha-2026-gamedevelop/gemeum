@echo off
setlocal enabledelayedexpansion

rem Steam 로비 경로로 실행한다. 방 만들기, 초대, 호스트 시작은 게임 안의 로비 패널에서 한다.
rem
rem   run-steam-lobby.bat          신원 무작위, 3인이 모여야 런이 시작된다
rem   run-steam-lobby.bat 1001     신원을 직접 준다
rem   run-steam-lobby.bat 1001 2   2인만으로 런을 시작한다 (PC가 둘뿐일 때)
rem
rem 인원은 방장(호스트)이 준 값만 쓴다. 참가자 쪽 값은 무시된다.
rem
rem 다른 스크립트와의 차이
rem   run-steam-host.bat    로비 없이 바로 호스트를 연다 (직접 접속 검증용)
rem   run-steam-client.bat  호스트 SteamID로 바로 접속한다 (직접 접속 검증용)
rem   run-steam-lobby.bat   로비를 거쳐 접속한다 (실제 출시 경로)
rem
rem 준비
rem   1. Steam 클라이언트가 실행 중이고 로그인되어 있어야 한다
rem   2. 개발 빌드여야 한다. 로비 패널과 실행 인자는 DEVELOPMENT_BUILD에만 있다
rem   3. 세 계정이 서로 친구여야 초대를 주고받을 수 있다

set "ROOT=%~dp0.."
set "PLATFORMID=%~1"
set "PLAYERS=%~2"

if not defined PLATFORMID set /a PLATFORMID=1000 + %RANDOM% %% 9000

rem 인원을 안 주면 씬에 저장된 값을 그대로 쓴다
set "PLAYERARGS="
if defined PLAYERS set "PLAYERARGS=-minplayers %PLAYERS%"

call :find_exe
if not defined EXE (
    echo 개발 빌드를 찾을 수 없다.
    echo   %~dp0gemeum.exe
    echo   %ROOT%\Builds\Development\gemeum.exe
    echo Unity에서 [그믐 - 빌드 - 개발용 빌드]로 다시 구울 것.
    echo 릴리스 빌드로는 실행 인자가 동작하지 않는다.
    pause
    exit /b 1
)

for %%I in ("%EXE%") do set "EXEDIR=%%~dpI"

rem SteamAPI.Init은 exe 옆의 steam_appid.txt에서 AppID를 읽는다
if not exist "%EXEDIR%steam_appid.txt" (
    if exist "%ROOT%\steam_appid.txt" (
        copy /y "%ROOT%\steam_appid.txt" "%EXEDIR%steam_appid.txt" >nul
    ) else (
        echo 경고: steam_appid.txt가 없다. Steam 초기화가 실패한다.
    )
)

set "LOGDIR=%EXEDIR%logs"
if not exist "%LOGDIR%" mkdir "%LOGDIR%"
set "LOGFILE=%LOGDIR%\steam-lobby.log"

if exist "%LOGFILE%" del /q "%LOGFILE%"

echo.
echo   Steam 로비 경로로 실행한다. 신원=%PLATFORMID%
if defined PLAYERS echo   %PLAYERS%인이 모이면 런을 시작한다.
echo.
echo   실행 후 화면 왼쪽의 [Steam 로비] 패널에서
echo     방장  : 방 만들기 - 친구 초대 - (인원이 차면) 호스트 시작
echo     참가자: Steam 오버레이에서 초대 수락
echo.

start "" /D "%EXEDIR%" "%EXE%" -steam -platformid %PLATFORMID% %PLAYERARGS% -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "%LOGFILE%"

echo   로그: %LOGFILE%
echo.
pause
exit /b 0

:find_exe
set "EXE="
if exist "%~dp0gemeum.exe" set "EXE=%~dp0gemeum.exe"
if not defined EXE if exist "%ROOT%\Builds\Development\gemeum.exe" set "EXE=%ROOT%\Builds\Development\gemeum.exe"
exit /b 0
