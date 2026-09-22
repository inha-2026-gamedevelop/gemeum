# 런처를 .NET 설치 없이 도는 단일 exe로 만들고, 서명해서 배포 저장소에 올린다
# 팀원 런처는 다음에 켤 때 스스로 새 런처를 받아 교체한다. 먼저 Launcher.UI.csproj의 <Version>을 올린다
#
#   .\publish-launcher.ps1 -ServerUrl http://169.212.47.146:8765   # 팀장 PC 서버 주소를 내장
#   .\publish-launcher.ps1 -Upload                                   # launcher-config.json 주소 그대로, R2로 올린다
#
# -ServerUrl을 주지 않고 내장 주소도 CHANGE-ME면, 팀원이 런처 설정에서 서버 주소를 직접 넣어야 한다
param(
    [string]$ServerUrl = "",
    [string]$Repo      = "",
    [string]$Key       = "$env:USERPROFILE\.geumeum\keys\release-key-1.private.pem",
    [string]$KeyId     = "release-key-1",
    [switch]$Upload
)
$ErrorActionPreference = "Stop"

$launcherDir = Split-Path -Parent $PSScriptRoot
$projectRoot = Split-Path -Parent $launcherDir
$outDir      = Join-Path $projectRoot "Builds\Launcher"
if ($Repo -eq "") { $Repo = Join-Path $projectRoot "Builds\PatchRepo" }

$configSource = Join-Path $launcherDir "Launcher.UI\launcher-config.json"
$configText   = [IO.File]::ReadAllText($configSource)
$configPath   = $configSource

if ($ServerUrl -ne "") {
    if ($ServerUrl -notmatch '^https?://[^\s"?#]+$') { throw "서버 주소 형식이 아니다: $ServerUrl" }
    $configText = [regex]::Replace($configText, '"baseUrl"\s*:\s*"[^"]*"', ('"baseUrl": "' + $ServerUrl.TrimEnd('/') + '"'))
    $configPath = Join-Path $projectRoot "Builds\Launcher-config\launcher-config.json"
    New-Item -ItemType Directory -Force (Split-Path $configPath) | Out-Null
    [IO.File]::WriteAllText($configPath, $configText, (New-Object Text.UTF8Encoding($false)))
}
elseif ($configText -match 'CHANGE-ME') {
    Write-Warning "내장 서버 주소가 없다. 팀원은 런처 설정에서 서버 주소를 직접 넣어야 한다"
}

dotnet publish (Join-Path $launcherDir "Launcher.UI") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none "-p:LauncherConfigPath=$configPath" -o $outDir --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "런처 publish 실패" }

$exe = Join-Path $outDir "GeumeumLauncher.exe"
Write-Host ("런처: {0} ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB)) -ForegroundColor Green

if (-not (Test-Path $Key)) { throw "서명 개인키가 없다: $Key" }

$toolDir = Join-Path $projectRoot "Builds\Tools\geumeum-pack"
dotnet build (Join-Path $launcherDir "Release.Packager") -c Release -o $toolDir --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "패키저 빌드 실패" }
$pack = Join-Path $toolDir "geumeum-pack.exe"

# 객체 저장소에 넣고 서명된 launcher/latest.json을 쓴다. 이미 더 높은 버전이 있으면 거부된다
& $pack publish-launcher --out $Repo --file $exe --key $Key --key-id $KeyId
if ($LASTEXITCODE -ne 0) { throw "런처 게시 실패 (Launcher.UI.csproj의 <Version>을 올렸는지 확인)" }

if ($ServerUrl -ne "") {
    Write-Host ("처음 받는 팀원: {0}/launcher/GeumeumLauncher.exe" -f $ServerUrl.TrimEnd('/')) -ForegroundColor Green
}

if ($Upload) {
    . (Join-Path $PSScriptRoot 'import-r2.ps1')
    & $pack upload-launcher --out $Repo
    if ($LASTEXITCODE -ne 0) { throw "런처 업로드 실패" }
}
