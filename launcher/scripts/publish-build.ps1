# 그믐 빌드를 런처 배포 저장소에 올린다. 게시 순서: 패키징 -> 채널 서명 -> 검증 -> (R2 업로드 -> 원격 검증)
#
#   .\publish-build.ps1                                  # Builds\Development를 internal 채널로 로컬 저장소에 게시
#   .\publish-build.ps1 -Notes notes.txt -Upload -Url https://pub-xxxx.r2.dev
#   .\publish-build.ps1 -Version 0.1.0                   # 테스트 버전을 직접 정한다(기본은 끝자리 +1, Builds\test-version.json)
#   .\publish-build.ps1 -Promote -ReleaseId win64-20260919-1907 -Channel stable -Upload -Url ...   # 재빌드 없이 승격
#
# R2 업로드에는 환경 변수 R2_ACCOUNT_ID, R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY, R2_BUCKET이 필요하다
param(
    [string]$Build     = "",
    [string]$Channel   = "internal",
    [string]$Repo      = "",
    [string]$Key       = "$env:USERPROFILE\.geumeum\keys\release-key-1.private.pem",
    [string]$KeyId     = "release-key-1",
    [string]$Notes     = "",
    [string]$ReleaseId = "",
    [string]$Url       = "",
    [string]$MinLauncher = "",   # 비우면 채널의 기존 값을 이어받는다
    [string]$Version   = "",     # 테스트 버전(예: 0.1.0). 비우면 마지막 게시 버전의 끝자리를 하나 올린다
    [switch]$Promote,
    [switch]$Upload
)
$ErrorActionPreference = "Stop"

$launcherDir = Split-Path -Parent $PSScriptRoot
$projectRoot = Split-Path -Parent $launcherDir
if ($Build -eq "") { $Build = Join-Path $projectRoot "Builds\Development" }
if ($Repo  -eq "") { $Repo  = Join-Path $projectRoot "Builds\PatchRepo" }
$publicKey = $Key -replace "\.private\.pem$", ".public.pem"

function Invoke-Pack {
    param([string[]]$Arguments)
    & $script:Pack @Arguments
    if ($LASTEXITCODE -ne 0) { throw "geumeum-pack $($Arguments[0]) 실패 (코드 $LASTEXITCODE)" }
}

# 패키저를 Release로 한 번 빌드해 쓴다. 실행 중 잠금을 피하려고 Builds\Tools로 복사한다
$toolDir = Join-Path $projectRoot "Builds\Tools\geumeum-pack"
dotnet build (Join-Path $launcherDir "Release.Packager") -c Release -o $toolDir --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "패키저 빌드 실패" }
$script:Pack = Join-Path $toolDir "geumeum-pack.exe"

if (-not (Test-Path $Key)) { throw "서명 개인키가 없다: $Key (geumeum-pack keygen으로 만들거나 팀장에게 받는다)" }
if ($Notes -ne "" -and -not (Test-Path $Notes)) { throw "패치 노트 파일이 없다: $Notes (빼고 실행해도 된다)" }

if (-not $Promote) {
    $identityPath = Join-Path $Build "gemeum_Data\StreamingAssets\build-identity.json"
    if (-not (Test-Path $identityPath)) { throw "빌드에 build-identity.json이 없다. Unity 메뉴 그믐/빌드로 다시 빌드한다: $identityPath" }
    $identity  = Get-Content $identityPath -Raw | ConvertFrom-Json
    $ReleaseId = $identity.releaseId

    # releaseId는 빌드 시각이라 같은 id면 같은 빌드다. 이미 패키징됐으면 게시만 이어서 한다
    $manifestPath = Join-Path $Repo "releases\$ReleaseId\manifest.json"
    if (Test-Path $manifestPath) {
        Write-Host "$ReleaseId 는 이미 패키징돼 있다. 채널 게시만 한다." -ForegroundColor Yellow
        if ($Notes -ne "") {
            New-Item -ItemType Directory -Force (Join-Path $Repo "notes") | Out-Null
            Copy-Item $Notes (Join-Path $Repo "notes\$ReleaseId.txt") -Force
        }
    }
    else {
        # 테스트 버전은 게시할 때 매긴다. Unity bundleVersion을 빌드마다 고치면 공용 ProjectSettings가 매번 바뀐다
        $versionFile = Join-Path (Split-Path -Parent $Repo) "test-version.json"
        $last = if (Test-Path $versionFile) { [version](Get-Content $versionFile -Raw | ConvertFrom-Json).lastVersion } else { [version]"0.0.0" }
        if ($Version -ne "") {
            if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "버전은 x.y.z 형식이어야 한다: $Version" }
            $next = [version]$Version
        }
        else {
            $next = New-Object Version $last.Major, $last.Minor, ($last.Build + 1)
        }
        if ($next -le $last) { throw "테스트 버전은 올라가기만 한다: 마지막 $last, 요청 $next" }

        # 빌드 폴더의 정체성 파일에 적어 게임 로그, manifest, 런처가 같은 번호를 보게 한다. 필드 구성은 패키저 계약 그대로
        $identity.displayVersion = "$next"
        $identityJson = '{"releaseId":"' + $identity.releaseId + '","displayVersion":"' + $next + '","protocolVersion":' + $identity.protocolVersion + ',"contentVersion":"' + $identity.contentVersion + '"}'
        [IO.File]::WriteAllText($identityPath, $identityJson, (New-Object Text.UTF8Encoding($false)))
        Write-Host "테스트 버전 $last → $next ($ReleaseId)" -ForegroundColor Cyan

        $packArgs = @("pack", "--build", $Build, "--out", $Repo)
        if ($Notes -ne "") { $packArgs += @("--notes", $Notes) }
        Invoke-Pack $packArgs

        [IO.File]::WriteAllText($versionFile, ('{"lastVersion":"' + $next + '","releaseId":"' + $ReleaseId + '"}'), (New-Object Text.UTF8Encoding($false)))
    }
}
elseif ($ReleaseId -eq "") {
    throw "-Promote에는 -ReleaseId가 필요하다"
}

# 로컬 채널의 다음 sequence. R2가 더 앞서 있으면 upload가 거부하고 알려 준다
$envelopePath = Join-Path $Repo "channels\$Channel\envelope.json"
$sequence = 1
if (Test-Path $envelopePath) {
    $envelope = Get-Content $envelopePath -Raw | ConvertFrom-Json
    $payload  = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($envelope.payloadBase64)) | ConvertFrom-Json
    $sequence = [long]$payload.sequence + 1
    if ($MinLauncher -eq "") { $MinLauncher = $payload.minLauncherVersion }

    # 같은 빌드, 같은 최소 런처 버전을 다시 게시하면 팀원 쪽에 바뀌는 것이 없다
    if ($payload.releaseId -eq $ReleaseId -and $payload.minLauncherVersion -eq $MinLauncher -and -not $Upload) {
        Write-Host "$Channel 채널은 이미 $ReleaseId 를 가리킨다 (seq $($payload.sequence)). 바꿀 것이 없다." -ForegroundColor Green
        exit 0
    }
}

if ($MinLauncher -eq "") { $MinLauncher = "1.0.0" }

Invoke-Pack @("publish-channel", "--out", $Repo, "--channel", $Channel, "--release-id", $ReleaseId,
              "--sequence", "$sequence", "--key", $Key, "--key-id", $KeyId, "--min-launcher", $MinLauncher)
Invoke-Pack @("verify-channel", "--out", $Repo, "--channel", $Channel, "--public-key", $publicKey, "--key-id", $KeyId)

if ($Upload) {
    . (Join-Path $PSScriptRoot 'import-r2.ps1')
    Invoke-Pack @("upload", "--out", $Repo, "--channel", $Channel)
    if ($Url -ne "") {
        Invoke-Pack @("verify-remote", "--url", $Url, "--channel", $Channel, "--public-key", $publicKey, "--key-id", $KeyId)
    }
    else {
        Write-Warning "-Url을 주지 않아 공개 주소 검증을 건너뛰었다"
    }
}

Write-Host "완료: $Channel seq $sequence -> $ReleaseId" -ForegroundColor Green
