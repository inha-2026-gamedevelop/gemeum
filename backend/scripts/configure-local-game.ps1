param([string]$Executable = (Join-Path $PSScriptRoot '../../Builds/Development/gemeum.exe'))
$ErrorActionPreference = 'Stop'
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gamePath = (Resolve-Path -LiteralPath $Executable).Path
$identityPath = Join-Path (Split-Path $gamePath) (([IO.Path]::GetFileNameWithoutExtension($gamePath)) + '_Data/StreamingAssets/build-identity.json')
$identity = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
if ($identity.releaseId -notmatch '^[A-Za-z0-9._-]{1,96}$') { throw 'Invalid build identity' }
$envFile = Join-Path $backendRoot '.env'
if (-not (Test-Path -LiteralPath $envFile)) { throw 'Run start-local.ps1 once to prepare the local database first.' }
$lines = @(Get-Content -LiteralPath $envFile | Where-Object { $_ -notmatch '^GEUMEUM_GAME_(EXECUTABLE|BUILD_ID)=' })
$lines += 'GEUMEUM_GAME_EXECUTABLE=' + $gamePath.Replace('\','/')
$lines += 'GEUMEUM_GAME_BUILD_ID=' + $identity.releaseId
[IO.File]::WriteAllLines($envFile, $lines, (New-Object Text.UTF8Encoding $false))
Write-Host "Configured game build: $($identity.releaseId). Restart API to apply. Listen hosting does not use a game worker."
