# 클라이언트 접속 결과를 요약한다. 접속은 호스트가 승인해야 성립하므로 조금 기다린다.
param(
    [Parameter(Mandatory = $true)][string]$LogPath,
    [int]$TimeoutSeconds = 45
)

$ErrorActionPreference = 'Continue'

Write-Host ""
Write-Host "  접속 결과를 기다린다 (최대 $($TimeoutSeconds)초)..."

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$connected = $false

while ((Get-Date) -lt $deadline) {
    if (Test-Path -LiteralPath $LogPath) {
        if (Select-String -Path $LogPath -Pattern 'ClientConnect|OnClientConnect|Connected to' -ErrorAction SilentlyContinue) { $connected = $true; break }
        if (Select-String -Path $LogPath -Pattern '초기화에 실패|연결 실패|Connect failed|Disconnected' -ErrorAction SilentlyContinue) { break }
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

$noise = '^(UnityEngine\.|Geumeum\.|Mirror\.|System\.|kcp2k\.|\s+at )'
$lines = Select-String -Path $LogPath -Pattern 'SteamTransportSelector|GeumeumNetworkManager|GameDBBootstrap|NetworkLaunchArgs|LobbyController' -ErrorAction SilentlyContinue
if ($lines) {
    foreach ($line in $lines) {
        if ($line.Line -notmatch $noise) { Write-Host ("  " + $line.Line) }
    }
} else {
    Write-Host "  아직 찾는 줄이 없다. 빌드가 최신인지 확인할 것." -ForegroundColor Yellow
}

Write-Host "  ==============================================="
Write-Host ""

if ($connected) {
    Write-Host "  접속에 성공한 것으로 보인다." -ForegroundColor Green
} else {
    Write-Host "  접속을 확인하지 못했다." -ForegroundColor Yellow
    Write-Host "  호스트가 켜져 있는지, SteamID64가 맞는지, 양쪽 Steam이 로그인 상태인지 확인할 것." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "  판정은 호스트 PC 로그에서 본다:" -ForegroundColor Cyan
Write-Host "    [GeumeumNetworkManager] 접속: connectionId=1, address=???" -ForegroundColor Cyan
Write-Host "    address가 SteamID64면 설계 전제가 성립한다." -ForegroundColor Cyan
