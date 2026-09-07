# 호스트 로그에서 SteamID64를 뽑아 콘솔에 띄우고 클립보드에 넣는다.
# 게임이 로그를 쓸 때까지 기다리는 것도 여기서 한다. 배치의 timeout은 대기 시간이 고정이라
# 부팅이 느린 PC에서 아직 비어 있는 로그를 읽고 아무것도 못 찾는 일이 있었다.
param(
    [Parameter(Mandatory = $true)][string]$LogPath,
    [int]$TimeoutSeconds = 45
)

$ErrorActionPreference = 'Continue'

Write-Host ""
Write-Host "  게임이 Steam에 붙을 때까지 기다린다 (최대 $($TimeoutSeconds)초)..."

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$steamId  = $null
$lobbyId  = $null

while ((Get-Date) -lt $deadline) {
    if (Test-Path -LiteralPath $LogPath) {
        $hit = Select-String -Path $LogPath -Pattern 'SteamID64=(\d+)' -ErrorAction SilentlyContinue | Select-Object -First 1
        $lobbyHit = Select-String -Path $LogPath -Pattern 'lobbyId=(\d+)' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($lobbyHit) { $lobbyId = $lobbyHit.Matches[0].Groups[1].Value }
        if ($hit) { $steamId = $hit.Matches[0].Groups[1].Value; break }

        # 초기화에 실패하면 ID가 영영 안 나오므로 그때도 빠져나온다
        if (Select-String -Path $LogPath -Pattern '초기화에 실패|부트 검증에 실패' -ErrorAction SilentlyContinue) { break }
    }
    Start-Sleep -Milliseconds 500
}

Write-Host ""
Write-Host "  ================== 로그 요약 =================="

if (-not (Test-Path -LiteralPath $LogPath)) {
    Write-Host "  로그가 만들어지지 않았다: $LogPath" -ForegroundColor Yellow
    Write-Host "  ==============================================="
    return
}

# 스택 트레이스는 원인 파악에 도움이 안 되므로 뺀다
$noise = '^(UnityEngine\.|Geumeum\.|Mirror\.|System\.|kcp2k\.|\s+at )'
$lines = Select-String -Path $LogPath -Pattern 'SteamTransportSelector|GeumeumNetworkManager|GameDBBootstrap|NetworkLaunchArgs' -ErrorAction SilentlyContinue
if ($lines) {
    foreach ($line in $lines) {
        if ($line.Line -notmatch $noise) { Write-Host ("  " + $line.Line) }
    }
} else {
    Write-Host "  아직 찾는 줄이 없다. 빌드가 최신인지 확인할 것." -ForegroundColor Yellow
}

Write-Host "  ==============================================="

if (-not $steamId) {
    Write-Host ""
    Write-Host "  SteamID64를 찾지 못했다." -ForegroundColor Yellow
    Write-Host "  Steam 클라이언트 실행 여부, steam_appid.txt, 빌드 최신 여부를 확인할 것." -ForegroundColor Yellow
    return
}

Write-Host ""
Write-Host "    내 SteamID64 : $steamId" -ForegroundColor Green
Write-Host ""
if ($lobbyId) {
    Write-Host "    로비 ID      : $lobbyId" -ForegroundColor Green
    Write-Host ""
}
Write-Host "  다른 PC에서 실행할 명령:"
if ($lobbyId) {
    Write-Host "    run-steam-client.bat $lobbyId" -ForegroundColor Cyan
    Write-Host "    (방에 들어와 기다린다. 인원이 차면 여기서 [호스트 시작]을 누를 것)" -ForegroundColor DarkGray
} else {
    Write-Host "    run-steam-client.bat $steamId" -ForegroundColor Cyan
    Write-Host "    (로비 없이 이미 열린 서버에 바로 붙는다)" -ForegroundColor DarkGray
}
Write-Host ""

# 클라이언트가 바로 쓸 수 있는 값을 넣는다. 로비가 있으면 로비 ID가 먼저다
$copyValue = if ($lobbyId) { $lobbyId } else { $steamId }

try {
    Set-Clipboard -Value $copyValue
    Write-Host "  (클립보드에 복사했다)" -ForegroundColor DarkGray
}
catch {
    Write-Host "  (클립보드 복사 실패, 위 값을 직접 옮길 것)" -ForegroundColor Yellow
}
