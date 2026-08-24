# 3인 멀티플레이 테스트용 실행 스크립트
# 빌드를 작은 창 여러 개로 띄우고 화면에 나란히 배치한다
#
# 사용법
#   powershell -ExecutionPolicy Bypass -File tools\run-3clients.ps1
#   powershell -ExecutionPolicy Bypass -File tools\run-3clients.ps1 -Count 2
#   powershell -ExecutionPolicy Bypass -File tools\run-3clients.ps1 -Kill

param(
    [int]    $Count = 3,
    [string] $ExePath = "Builds\Windows\Gemeum.exe",
    [switch] $Kill
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$exe      = Join-Path $repoRoot $ExePath
$procName = [System.IO.Path]::GetFileNameWithoutExtension($exe)

if ($Kill)
{
    $running = Get-Process -Name $procName -ErrorAction SilentlyContinue
    if ($running)
    {
        $running | Stop-Process -Force
        Write-Output "$($running.Count)개 종료"
    }
    else
    {
        Write-Output "실행 중인 인스턴스 없음"
    }
    return
}

if (-not (Test-Path $exe))
{
    Write-Output "빌드를 찾을 수 없다: $exe"
    Write-Output "Unity에서 Build를 먼저 실행할 것"
    return
}

# 이미 떠 있으면 포트가 겹치므로 먼저 정리한다
$stale = Get-Process -Name $procName -ErrorAction SilentlyContinue
if ($stale)
{
    $stale | Stop-Process -Force
    Start-Sleep -Milliseconds 600
    Write-Output "이전 인스턴스 $($stale.Count)개 정리"
}

# Unity 빌드에는 창 위치를 지정하는 실행 인자가 없어 Win32 API로 옮긴다
if (-not ('Win32Window' -as [type]))
{
    $signature = @"
[DllImport("user32.dll")]
public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
"@
    Add-Type -MemberDefinition $signature -Name 'Win32Window' -Namespace '' -PassThru | Out-Null
}

Add-Type -AssemblyName System.Windows.Forms
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea

# 화면을 넘지 않도록 창 크기를 역산한다
$margin = 8
$winW   = [int](($screen.Width - $margin * ($Count + 1)) / $Count)
$winH   = [int]($winW * 9 / 16)
if ($winH -gt ($screen.Height - $margin * 2))
{
    $winH = $screen.Height - $margin * 2
    $winW = [int]($winH * 16 / 9)
}
$top = $screen.Y + [int](($screen.Height - $winH) / 2)

Write-Output "화면 $($screen.Width)x$($screen.Height), 창 ${winW}x${winH} $Count개"

$logDir = Join-Path $repoRoot 'build\logs'
if (-not (Test-Path $logDir))
{
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

$started = @()
for ($i = 0; $i -lt $Count; $i++)
{
    $log = Join-Path $logDir "client$i.log"
    $launchArgs = @(
        '-screen-fullscreen', '0',
        '-screen-width',  "$winW",
        '-screen-height', "$winH",
        '-logFile', $log
    )
    $started += Start-Process -FilePath $exe -ArgumentList $launchArgs -PassThru
    Start-Sleep -Milliseconds 500
}

# 프로세스 시작 직후에는 창 핸들이 0이라 잡힐 때까지 기다린다
foreach ($p in $started)
{
    for ($t = 0; $t -lt 40; $t++)
    {
        $p.Refresh()
        if ($p.MainWindowHandle -ne 0)
        {
            break
        }
        Start-Sleep -Milliseconds 250
    }
}

for ($i = 0; $i -lt $started.Count; $i++)
{
    $p = $started[$i]
    $p.Refresh()
    if ($p.MainWindowHandle -eq 0)
    {
        Write-Output "  [$i] PID $($p.Id) 창 핸들을 잡지 못했다, 직접 옮길 것"
        continue
    }

    $left = $screen.X + $margin + ($winW + $margin) * $i
    [void][Win32Window]::MoveWindow($p.MainWindowHandle, $left, $top, $winW, $winH, $true)
    Write-Output "  [$i] PID $($p.Id) -> ($left, $top)   로그 build\logs\client$i.log"
}

Write-Output ""
Write-Output "첫 창에서 Host, 나머지에서 Client를 누른다"
Write-Output "종료: powershell -ExecutionPolicy Bypass -File tools\run-3clients.ps1 -Kill"
