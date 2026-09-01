@echo off
setlocal

rem 다른 PC의 호스트에 클라이언트로 접속한다. 3인 테스트(M3)용.
rem
rem 사용법
rem   run-client.bat 118.37.238.46         기본 포트 7777
rem   run-client.bat 118.37.238.46 7777    포트 지정
rem   run-client.bat                       실행 후 IP를 물어본다

set "ROOT=%~dp0.."
set "IP=%~1"
set "PORT=%~2"
if "%PORT%"=="" set "PORT=7777"

if "%IP%"=="" set /p "IP=호스트 IP를 입력한다 (예: 118.37.238.46): "
if "%IP%"=="" (
    echo IP가 없어 종료한다.
    pause
    exit /b 1
)

call :find_exe
if not defined EXE (
    echo 빌드를 찾을 수 없다.
    echo   %ROOT%\Builds\Development\gemeum.exe
    echo   %ROOT%\Builds\Windows64\gemeum.exe
    echo Unity에서 [그믐 - 빌드 - 개발용 빌드]를 먼저 실행할 것.
    pause
    exit /b 1
)

rem 로그는 실행 파일 옆에 남긴다. tools에서 돌리든 빌드 폴더에서 돌리든 같은 규칙이 된다
for %%I in ("%EXE%") do set "LOGDIR=%%~dpIlogs"
if not exist "%LOGDIR%" mkdir "%LOGDIR%"

echo.
echo   %IP%:%PORT% 로 접속한다 ^(UDP^)
echo   3명이 모두 접속해야 런이 시작된다.
echo.

start "" "%EXE%" -client %IP% -port %PORT% -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "%LOGDIR%\client.log"
echo 클라이언트를 시작했다. 로그: %LOGDIR%\client.log
exit /b 0

:find_exe
rem 빌드 폴더에 함께 복사된 경우를 먼저 본다. 그다음이 저장소 tools에서 실행한 경우다
set "EXE="
if exist "%~dp0gemeum.exe" set "EXE=%~dp0gemeum.exe"
if not defined EXE if exist "%ROOT%\Builds\Development\gemeum.exe" set "EXE=%ROOT%\Builds\Development\gemeum.exe"
if not defined EXE if exist "%ROOT%\Builds\Windows64\gemeum.exe" set "EXE=%ROOT%\Builds\Windows64\gemeum.exe"
exit /b 0
