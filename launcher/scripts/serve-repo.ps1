# 이 PC를 배포 서버로 연다(R2 전까지). 창을 닫거나 Ctrl+C로 끄면 팀원이 업데이트를 받지 못한다
#
#   .\serve-repo.ps1              # Builds\PatchRepo를 8765 포트로 LAN, 외부에 연다
#   .\serve-repo.ps1 -Port 9000
#
# 처음 실행하면 Windows 방화벽 허용 창이 뜬다. 팀원이 다른 네트워크에 있으면 "공용 네트워크"에도 체크한다
param(
    [int]$Port    = 8765,
    [string]$Repo = ""
)
$ErrorActionPreference = "Stop"

$launcherDir = Split-Path -Parent $PSScriptRoot
$projectRoot = Split-Path -Parent $launcherDir
if ($Repo -eq "") { $Repo = Join-Path $projectRoot "Builds\PatchRepo" }
if (-not (Test-Path (Join-Path $Repo "channels"))) {
    throw "게시된 배포가 없다: $Repo  (먼저 .\publish-build.ps1 실행)"
}

# 서버가 도는 동안 패키저를 다시 빌드해도 막히지 않게 별도 폴더에서 실행한다
$toolDir = Join-Path $projectRoot "Builds\Tools\geumeum-serve"

# 이미 켜진 서버가 있으면 그 파일을 잡고 있어 다시 빌드할 수 없다. 두 번 켤 이유도 없다
$running = Get-Process geumeum-pack -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$toolDir\*" }
if ($running) {
    Write-Host ("서버가 이미 켜져 있다 (PID {0}). 다시 켜려면 그 창을 닫거나 'Stop-Process -Id {0}' 후 다시 실행한다." -f ($running.Id -join ", ")) -ForegroundColor Yellow
    exit 0
}

$listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($listener) {
    $owner = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
    throw ("{0} 포트를 다른 프로그램이 쓰고 있다: {1} (PID {2}). -Port로 다른 포트를 쓴다" -f $Port, $owner.ProcessName, $listener.OwningProcess)
}

dotnet build (Join-Path $launcherDir "Release.Packager") -c Release -o $toolDir --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "패키저 빌드 실패" }

& (Join-Path $toolDir "geumeum-pack.exe") serve --out $Repo --port $Port --lan
