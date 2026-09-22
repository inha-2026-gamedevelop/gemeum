# 로컬 빌드, 런처 버전을 배포 서버와 비교하고, 다르면 서버에 동기화한다
#
#   .\sync-server.ps1 -Check          # 비교만 한다. 아무것도 바꾸지 않는다
#   .\sync-server.ps1                 # 다르면 게시한다. 패치 노트는 두 빌드 사이의 git 커밋으로 만든다
#   .\sync-server.ps1 -Notes n.txt    # 패치 노트를 직접 준다
#   .\sync-server.ps1 -BumpLauncher   # 런처 코드가 바뀌었는데 버전이 그대로면 패치 버전을 올려 같이 게시한다
#   .\sync-server.ps1 -Force          # 로컬 빌드가 서버보다 오래돼도 게시한다(되돌리기)
#   .\sync-server.ps1 -Only game      # 게임만(또는 launcher만) 맞춘다
#   .\sync-server.ps1 -Version 0.1.0  # 테스트 버전을 직접 정한다. 기본은 마지막 게시 버전 끝자리 +1 (0.0.1부터)
#
# 게시 기록은 Builds\publish-history.md에 한 줄씩 남는다
param(
    [switch]$Check,
    [string]$Notes     = "",
    [switch]$BumpLauncher,
    [switch]$Force,
    [string]$ServerUrl = "",
    [string]$Channel   = "internal",
    [ValidateSet("all", "game", "launcher")]
    [string]$Only      = "all",
    [string]$Build     = "",
    [string]$Repo      = "",
    [string]$Version   = "",
    [switch]$Upload
)
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$launcherDir = Split-Path -Parent $PSScriptRoot
$projectRoot = Split-Path -Parent $launcherDir
$build       = if ($Build) { $Build } else { Join-Path $projectRoot "Builds\Development" }
$repo        = if ($Repo)  { $Repo }  else { Join-Path $projectRoot "Builds\PatchRepo" }
$workDir     = Join-Path $projectRoot "Builds\Launcher-config"
$csproj      = Join-Path $launcherDir "Launcher.UI\Launcher.UI.csproj"
# 기록은 저장소 옆에 둔다. 평소에는 Builds\publish-history.md
$history     = Join-Path (Split-Path -Parent $repo) "publish-history.md"
# 게임 테스트 버전 기록. publish-build.ps1이 게시할 때 갱신한다
$versionFile = Join-Path (Split-Path -Parent $repo) "test-version.json"
New-Item -ItemType Directory -Force $workDir | Out-Null

function Open-Envelope([string]$json) {
    $envelope = $json | ConvertFrom-Json
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($envelope.payloadBase64)) | ConvertFrom-Json
}

function Read-LocalEnvelope([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    return Open-Envelope ([IO.File]::ReadAllText($path))
}

function Read-RemoteText([string]$url) {
    try {
        $response = Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 5
        return [Text.Encoding]::UTF8.GetString($response.RawContentStream.ToArray())
    }
    catch { return $null }
}

# releaseId 뒤쪽 yyyyMMdd-HHmm을 숫자로 비교한다. 형식이 다르면 비교하지 않는다
function Get-ReleaseOrder([string]$id) {
    if ($id -match '-(\d{8})-(\d{3,4})$') { return [long]($Matches[1] + $Matches[2].PadLeft(4, '0')) }
    return $null
}

function Get-Commit([string]$contentVersion) {
    if ($contentVersion -match '^([0-9a-f]{7,40})') { return $Matches[1] }
    return $null
}

# --- 로컬 게임 빌드 -----------------------------------------
$identityPath = Join-Path $build "gemeum_Data\StreamingAssets\build-identity.json"
$localBuild   = if (Test-Path $identityPath) { Get-Content $identityPath -Raw | ConvertFrom-Json } else { $null }
$head         = (git -C $projectRoot rev-parse --short=12 HEAD 2>$null)

# --- 배포 저장소, 서버 ---------------------------------------
$envelopePath = Join-Path $repo "channels\$Channel\envelope.json"
$repoChannel  = Read-LocalEnvelope $envelopePath
$repoRelease  = $null
if ($repoChannel) {
    $manifestPath = Join-Path $repo "releases\$($repoChannel.releaseId)\manifest.json"
    if (Test-Path $manifestPath) { $repoRelease = Get-Content $manifestPath -Raw | ConvertFrom-Json }
}

if ($ServerUrl -eq "") {
    $configPath = Join-Path $launcherDir "Launcher.UI\launcher-config.json"
    if (Test-Path $configPath) { $ServerUrl = (Get-Content $configPath -Raw | ConvertFrom-Json).baseUrl }
}
$ServerUrl = $ServerUrl.TrimEnd('/')
$r2ConfigPath = Join-Path $env:USERPROFILE '.geumeum\r2-config.json'
$useR2 = [bool]$Upload
if (Test-Path $r2ConfigPath) {
    $r2Settings = Get-Content $r2ConfigPath -Raw | ConvertFrom-Json
    if ($ServerUrl -eq $r2Settings.baseUrl.TrimEnd('/')) { $useR2 = $true }
}

$serverText    = if ($ServerUrl) { Read-RemoteText "$ServerUrl/channels/$Channel/envelope.json" } else { $null }
$serverChannel = if ($serverText) { Open-Envelope $serverText } else { $null }
$serverMatchesRepo = $serverText -and (Test-Path $envelopePath) -and ($serverText -eq [IO.File]::ReadAllText($envelopePath))

# --- 런처 --------------------------------------------------
$csprojText       = [IO.File]::ReadAllText($csproj)
$launcherVersion  = [version]([regex]::Match($csprojText, '<Version>([^<]+)</Version>').Groups[1].Value)
$publishedLauncher = Read-LocalEnvelope (Join-Path $repo "launcher\latest.json")
$publishedVersion  = if ($publishedLauncher) { [version]$publishedLauncher.version } else { $null }
$remoteLauncherText = if ($ServerUrl) { Read-RemoteText "$ServerUrl/launcher/latest.json" } else { $null }
$remoteLauncher = if ($remoteLauncherText) { Open-Envelope $remoteLauncherText } else { $null }

# 게시 이후 런처 코드가 바뀌었는지는 소스 수정 시각으로 본다. 커밋 전 변경도 잡힌다
$sourceDirs = "Launcher.UI", "Patch.Client", "Patch.Core" | ForEach-Object { Join-Path $launcherDir $_ }
$newestSource = Get-ChildItem $sourceDirs -Recurse -File -Include *.cs, *.xaml, *.csproj, *.json |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
$launcherChanged = $publishedLauncher -and $newestSource -and
    ($newestSource.LastWriteTimeUtc -gt ([DateTimeOffset]::Parse($publishedLauncher.issuedAtUtc)).UtcDateTime)

# --- 판단 --------------------------------------------------
$gameAction = "none"
if (-not $localBuild) {
    $gameAction = "no-local-build"
}
elseif (-not $repoChannel -or $localBuild.releaseId -ne $repoChannel.releaseId) {
    $localOrder  = Get-ReleaseOrder $localBuild.releaseId
    $remoteOrder = if ($repoChannel) { Get-ReleaseOrder $repoChannel.releaseId } else { $null }
    if ($remoteOrder -and $localOrder -and $localOrder -lt $remoteOrder -and -not $Force) { $gameAction = "local-older" }
    else { $gameAction = "publish" }
}

if ($Only -eq "launcher") { $gameAction = "skipped" }

$launcherAction = "none"
if ($Only -eq "game") { $launcherAction = "skipped" }
elseif (-not $publishedVersion -or $launcherVersion -gt $publishedVersion) { $launcherAction = "publish" }
elseif ($launcherVersion -lt $publishedVersion) { $launcherAction = "csproj-older" }
elseif ($launcherChanged) { $launcherAction = if ($BumpLauncher) { "bump" } else { "changed-not-bumped" } }

$lastTestVersion = if (Test-Path $versionFile) { [version](Get-Content $versionFile -Raw | ConvertFrom-Json).lastVersion } else { [version]"0.0.0" }
$nextTestVersion = if ($Version -ne "") { [version]$Version } else { New-Object Version $lastTestVersion.Major, $lastTestVersion.Minor, ($lastTestVersion.Build + 1) }
if ($Version -ne "" -and ($Version -notmatch '^\d+\.\d+\.\d+$' -or $nextTestVersion -le $lastTestVersion)) {
    throw "테스트 버전은 x.y.z 형식이고 마지막 게시 $lastTestVersion 보다 커야 한다: $Version"
}

# --- 보고 --------------------------------------------------
Write-Host ""
Write-Host "[게임 빌드]"
if ($localBuild) {
    Write-Host ("  로컬   : {0}  (표시 {1}, 커밋 {2})" -f $localBuild.releaseId, $localBuild.displayVersion, $localBuild.contentVersion)
    if ($head -and (Get-Commit $localBuild.contentVersion) -ne $head) { Write-Host "           ※ 이 빌드는 HEAD($head)가 아닌 커밋으로 만들어졌다" -ForegroundColor Yellow }
}
else { Write-Host "  로컬   : 빌드 없음 또는 build-identity.json 없음 ($build)" -ForegroundColor Yellow }
if ($repoChannel) {
    Write-Host ("  게시됨 : {0}  (seq {1}, 최소 런처 {2}, 커밋 {3})" -f $repoChannel.releaseId, $repoChannel.sequence, $repoChannel.minLauncherVersion, $(if ($repoRelease) { $repoRelease.contentVersion } else { "?" }))
}
else { Write-Host "  게시됨 : 없음" }
Write-Host ("  테스트 버전: 마지막 게시 {0}{1}" -f $(if ($lastTestVersion -eq [version]"0.0.0") { "없음" } else { $lastTestVersion }), $(if ($gameAction -eq "publish") { " → 이번 게시 $nextTestVersion" } else { "" }))

Write-Host "[런처]"
Write-Host ("  소스   : {0}{1}" -f $launcherVersion, $(if ($launcherChanged) { "  (게시 이후 코드 수정 있음)" } else { "" }))
Write-Host ("  게시됨 : {0}" -f $(if ($publishedVersion) { $publishedVersion } else { "없음" }))
Write-Host ("  원격   : {0}" -f $(if ($remoteLauncher) { $remoteLauncher.version } else { "응답 없음" }))

Write-Host "[서버]"
if (-not $ServerUrl) { Write-Host "  주소를 모른다. -ServerUrl로 준다" -ForegroundColor Yellow }
elseif (-not $serverText) { Write-Host "  $ServerUrl 채널 응답 없음 — 주소, 게시 상태, 연결을 확인한다" -ForegroundColor Yellow }
elseif ($serverMatchesRepo) { Write-Host "  $ServerUrl 정상, 로컬 저장소와 같은 내용을 내보내는 중" }
else { Write-Host "  $ServerUrl 응답은 있으나 로컬 저장소와 내용이 다르다 (seq $($serverChannel.sequence) → $($serverChannel.releaseId))" -ForegroundColor Yellow }

$gameText = @{
    "none"           = "같다 — 할 일 없음"
    "publish"        = "다르다 — 로컬 빌드를 게시한다"
    "local-older"    = "로컬 빌드가 서버보다 오래됐다 — 게시하지 않는다(되돌리려면 -Force)"
    "no-local-build" = "로컬 빌드가 없다 — Unity 메뉴 그믐/빌드/개발용 빌드 먼저"
    "skipped"        = "이번엔 확인하지 않음(-Only)"
}[$gameAction]
$launcherText = @{
    "none"               = "같다 — 할 일 없음"
    "publish"            = "소스 버전이 높다 — 런처를 게시한다"
    "bump"               = "코드가 바뀌었다 — 패치 버전을 올려 게시한다"
    "changed-not-bumped" = "코드가 바뀌었는데 버전이 그대로다 — -BumpLauncher로 올려 게시할 수 있다"
    "csproj-older"       = "소스 버전이 게시본보다 낮다 — Launcher.UI.csproj <Version>을 확인한다"
    "skipped"            = "이번엔 확인하지 않음(-Only)"
}[$launcherAction]
Write-Host ""
Write-Host "판단: 게임 $gameText"
Write-Host "      런처 $launcherText"

if ($Check) { exit 0 }

if (-not (Test-Path $history)) {
    Set-Content $history -Encoding UTF8 -Value @(
        "# 배포 서버 게시 기록",
        "",
        "sync-server.ps1이 게시할 때마다 한 줄씩 남긴다.",
        "",
        "| 시각 | 종류 | 버전(releaseId) | 표시 버전 | 소스 커밋 |",
        "|---|---|---|---|---|")
}

$didSomething = $false

# --- 런처 먼저 ---------------------------------------------
if ($launcherAction -in "publish", "bump") {
    if (-not $ServerUrl) { throw "런처를 게시하려면 서버 주소가 필요하다: -ServerUrl" }
    if ($launcherAction -eq "bump") {
        $next = New-Object Version $launcherVersion.Major, $launcherVersion.Minor, ([Math]::Max($launcherVersion.Build, 0) + 1)
        $csprojText = $csprojText -replace '<Version>[^<]+</Version>', "<Version>$next</Version>"
        [IO.File]::WriteAllText($csproj, $csprojText, (New-Object Text.UTF8Encoding($false)))
        Write-Host "런처 버전 $launcherVersion → $next" -ForegroundColor Cyan
        $launcherVersion = $next
    }
    & (Join-Path $PSScriptRoot "publish-launcher.ps1") -ServerUrl $ServerUrl -Repo $repo
    $didSomething = $true
    Add-Content $history ("| {0:yyyy-MM-dd HH:mm} | 런처 | {1} | | |" -f (Get-Date), $launcherVersion) -Encoding UTF8
}

# --- 게임 빌드 ---------------------------------------------
if ($gameAction -eq "publish") {
    if ($Notes -eq "") {
        $Notes = Join-Path $workDir ("notes-{0}.txt" -f $localBuild.releaseId)
        $lines = New-Object Collections.Generic.List[string]
        $lines.Add(("그믐 테스트 빌드 {0} ({1})" -f $nextTestVersion, $localBuild.releaseId))
        $lines.Add("")

        $from = if ($repoRelease) { Get-Commit $repoRelease.contentVersion } else { $null }
        $to   = Get-Commit $localBuild.contentVersion
        if ($from -and $to -and $from -ne $to) {
            $log = git -C $projectRoot log --no-merges --format="- %s" "$from..$to" 2>$null | Select-Object -First 30
            if ($log) { $log | ForEach-Object { $lines.Add($_) } }
        }
        if ($lines.Count -eq 2) { $lines.Add("- 이전 빌드와 같은 커밋에서 다시 만든 빌드") }
        if ($localBuild.contentVersion -like "*-dirty") { $lines.Add("- 커밋하지 않은 변경이 포함된 빌드") }

        [IO.File]::WriteAllText($Notes, ($lines -join "`n") + "`n", (New-Object Text.UTF8Encoding($false)))
        Write-Host "패치 노트 자동 작성: $Notes" -ForegroundColor Cyan
        Get-Content $Notes -Encoding UTF8 | ForEach-Object { Write-Host "  $_" }
    }

    & (Join-Path $PSScriptRoot "publish-build.ps1") -Notes $Notes -Channel $Channel -Build $build -Repo $repo -Version "$nextTestVersion"
    $didSomething = $true
    Add-Content $history ("| {0:yyyy-MM-dd HH:mm} | 게임 | {1} | {2} | {3} |" -f (Get-Date), $localBuild.releaseId, $nextTestVersion, $localBuild.contentVersion) -Encoding UTF8
}

if ($useR2) {
    . (Join-Path $PSScriptRoot 'import-r2.ps1')
    $pack = Join-Path $projectRoot 'Builds\Tools\geumeum-pack\geumeum-pack.exe'
    if (-not (Test-Path $pack)) { throw '패키저를 먼저 빌드해야 한다' }
    $localLatestPath = Join-Path $repo 'launcher\latest.json'
    $launcherNeedsUpload = $Upload -or -not (Test-Path $localLatestPath) -or
        ($remoteLauncherText -ne [IO.File]::ReadAllText($localLatestPath))
    $gameNeedsUpload = $Upload -or -not (Test-Path $envelopePath) -or
        ($serverText -ne [IO.File]::ReadAllText($envelopePath))
    if ($Only -ne 'game' -and $launcherNeedsUpload) {
        & $pack upload-launcher --out $repo
        if ($LASTEXITCODE -ne 0) { throw 'R2 런처 업로드 실패' }
        $remoteLatest = Read-RemoteText "$ServerUrl/launcher/latest.json"
        if ($remoteLatest -ne [IO.File]::ReadAllText((Join-Path $repo 'launcher\latest.json'))) {
            throw 'R2 공개 주소의 런처 서명 문서가 로컬 게시본과 다르다'
        }
    }
    if ($Only -ne 'launcher' -and $gameNeedsUpload) {
        & $pack upload --out $repo --channel $Channel
        if ($LASTEXITCODE -ne 0) { throw 'R2 게임 업로드 실패' }
    }
    & $pack verify-remote --url $ServerUrl --channel $Channel --public-key "$env:USERPROFILE\.geumeum\keys\release-key-1.public.pem" --key-id release-key-1
    if ($LASTEXITCODE -ne 0) { throw 'R2 원격 검증 실패' }
    $didSomething = $true
}

if (-not $didSomething) {
    Write-Host ""
    Write-Host "동기화할 것이 없다." -ForegroundColor Green
    exit 0
}

# --- 결과 확인 ---------------------------------------------
$after = if ($ServerUrl) { Read-RemoteText "$ServerUrl/channels/$Channel/envelope.json" } else { $null }
Write-Host ""
if (-not $after) {
    Write-Host "로컬 게시는 끝났지만 원격 채널에 연결하지 못했다. 배포 주소와 업로드 상태를 확인한다." -ForegroundColor Yellow
}
elseif ($after -eq [IO.File]::ReadAllText($envelopePath)) {
    $payload = Open-Envelope $after
    Write-Host ("서버 동기화 완료: {0} seq {1} → {2}" -f $Channel, $payload.sequence, $payload.releaseId) -ForegroundColor Green
}
else {
    Write-Host "서버 응답이 로컬 저장소와 다르다. 서버가 다른 폴더를 내보내는지 확인한다." -ForegroundColor Yellow
}
