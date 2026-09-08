@echo off
setlocal enabledelayedexpansion

rem 이 PC를 호스트로 띄운다. 다른 PC 3인 테스트(M3)용.
rem 한 PC에서 3개를 띄우는 M2 테스트는 tools\run-3clients.ps1을 쓴다.
rem
rem 사용법
rem   run-host.bat            기본 포트 7777, 시작 인원 3
rem   run-host.bat 7777       포트 지정
rem   run-host.bat 7777 2     포트와 시작 인원 지정

set "ROOT=%~dp0.."
set "PORT=%~1"
if "%PORT%"=="" set "PORT=7777"
rem 시작 인원. 안 넘기면 씬에 박힌 값이 이기고, 그 값이 1이면 호스트가 혼자 런을 시작해
rem 뒤따라 붙는 클라이언트가 전부 AdmissionLocked 로 거부된다
set "PLAYERS=%~2"
if "%PLAYERS%"=="" set "PLAYERS=3"

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

rem 팀원에게 알려줄 주소를 먼저 보여준다
echo.
echo   팀원이 접속할 주소 ^(IPv4^)
for /f "tokens=2 delims=:" %%A in ('ipconfig ^| findstr /c:"IPv4"') do (
    set "IPADDR=%%A"
    echo     !IPADDR: =!   포트 %PORT% ^(UDP^)
)
echo.
echo   주의: UDP %PORT% 인바운드 방화벽 규칙이 필요하다.
echo         테스트가 끝나면 규칙을 끌 것.
echo.

start "" "%EXE%" -host -port %PORT% -minplayers %PLAYERS% -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "%LOGDIR%\host.log"
echo 호스트를 시작했다. 로그: %LOGDIR%\host.log
exit /b 0

:find_exe
rem 빌드 폴더에 함께 복사된 경우를 먼저 본다. 그다음이 저장소 tools에서 실행한 경우다
set "EXE="
if exist "%~dp0gemeum.exe" set "EXE=%~dp0gemeum.exe"
if not defined EXE if exist "%ROOT%\Builds\Development\gemeum.exe" set "EXE=%ROOT%\Builds\Development\gemeum.exe"
if not defined EXE if exist "%ROOT%\Builds\Windows64\gemeum.exe" set "EXE=%ROOT%\Builds\Windows64\gemeum.exe"
exit /b 0
