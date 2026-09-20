# 현재 Windows 사용자에게 암호화된 게시 자격 증명을 프로세스 환경으로만 읽는다.
$r2ConfigPath = Join-Path $env:USERPROFILE '.geumeum\r2-config.json'
$r2CredentialPath = Join-Path $env:USERPROFILE '.geumeum\r2-credentials.xml'
if (Test-Path $r2ConfigPath) {
    $r2Config = Get-Content $r2ConfigPath -Raw | ConvertFrom-Json
    if (-not $env:R2_ACCOUNT_ID) { $env:R2_ACCOUNT_ID = $r2Config.accountId }
    if (-not $env:R2_BUCKET) { $env:R2_BUCKET = $r2Config.bucket }
}
if ((-not $env:R2_ACCESS_KEY_ID -or -not $env:R2_SECRET_ACCESS_KEY) -and (Test-Path $r2CredentialPath)) {
    if ($env:R2_ACCESS_KEY_ID -or $env:R2_SECRET_ACCESS_KEY) { throw 'R2 키 환경 변수는 두 값을 함께 지정해야 한다' }
    $r2Credential = Import-Clixml $r2CredentialPath
    $env:R2_ACCESS_KEY_ID = $r2Credential.UserName
    $env:R2_SECRET_ACCESS_KEY = $r2Credential.GetNetworkCredential().Password
    $r2Credential = $null
}
foreach ($r2Name in 'R2_ACCOUNT_ID','R2_BUCKET','R2_ACCESS_KEY_ID','R2_SECRET_ACCESS_KEY') {
    if (-not [Environment]::GetEnvironmentVariable($r2Name)) { throw "$r2Name 설정이 필요하다" }
}
