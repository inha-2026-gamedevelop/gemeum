# 3인 멀티플레이 테스트용 실행 스크립트
# 빌드를 작은 창 여러 개로 띄우고 화면에 나란히 배치한다
#
# 사용법
#   powershell -ExecutionPolicy Bypass -File tools\run-3clients.ps1
#   powershell -ExecutionPolicy Bypass -File tools\run-3clients.ps1 -Count 2
#   powershell -ExecutionPolicy Bypass -File tools\run-3clients.ps1 -Kill

param(
    [int]    $Count = 3,
    [string] $ExePath = "",
    [switch] $Kill
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

# 실행 인자(-platformid 등)는 개발 빌드에서만 동작한다. NetworkLaunchArgs가 DEVELOPMENT_BUILD로 감싸여 있다
if ([string]::IsNullOrEmpty($ExePath))
{
    $candidates = @('Builds\Development\gemeum.exe', 'Builds\Windows64\Gemeum.exe')
    foreach ($candidate in $candidates)
    {
        if (Test-Path (Join-Path $repoRoot $candidate)) { $ExePath = $candidate; break }
    }

    if ([string]::IsNullOrEmpty($ExePath)) { $ExePath = $candidates[0] }
}

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
    Write-Output "Unity에서 [그믐 - 빌드 - 개발용 빌드]를 먼저 실행할 것"
    Write-Output "릴리스 빌드로는 -platformid 같은 실행 인자가 동작하지 않는다"
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

$logDir = Join-Path (Split-Path -Parent $exe) 'logs'
if (-not (Test-Path $logDir))
{
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

$started = @()
for ($i = 0; $i -lt $Count; $i++)
{
    $log = Join-Path $logDir "client$i.log"
    # 창마다 고정된 신원을 준다. 없으면 재접속 때 다른 사람으로 취급되어
    # 자리와 역할 복구가 동작하지 않는다. 값은 창 순서에 묶여 다시 켜도 같다
    $platformId = 1001 + $i

    # -nocursorlock 이 없으면 창 하나가 마우스를 독점해 나머지 둘이 카메라를 못 돌린다.
    # 이 플래그를 만든 이유가 정확히 한 PC 3창 테스트라, 여기서 안 넘기면 만든 의미가 없다.
    # 켜지면 커서 잠금 없이 Alt를 누르는 동안에만 카메라가 돈다(유니티 씬뷰와 같다)
    # -minplayers 를 안 넘기면 씬에 박힌 _devMinPlayers 가 이긴다. 그 값이 1이면
    # 호스트가 혼자 런을 시작하고 입장을 잠가버려, 뒤따라 뜬 창들이 전부
    # AdmissionLocked 로 거부된다. 창 수와 시작 인원을 여기서 같이 맞춘다
    $launchArgs = @(
        '-platformid', "$platformId",
        '-minplayers', "$Count",
        '-nocursorlock',
        '-screen-fullscreen', '0',
        '-screen-width',  "$winW",
        '-screen-height', "$winH",
        '-logFile', $log
    )
    Write-Output "  $i 번 창 platformId=$platformId"
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
Write-Output "카메라는 Alt를 누르는 동안에만 돈다 (-nocursorlock). 3창이 마우스를 나눠 쓰기 위한 것이다"
Write-Output "종료: powershell -ExecutionPolicy Bypass -File tools\run-3clients.ps1 -Kill"
