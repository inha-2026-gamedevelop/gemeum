$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$repo = Join-Path $projectRoot 'backend/.local/account-patch/repo'
if (-not (Test-Path -LiteralPath (Join-Path $repo 'channels/internal/envelope.json'))) {
    throw 'Prepare the local patch repository first; see backend/LOCAL_PLAY.md.'
}
Push-Location $projectRoot
try {
    dotnet run --project launcher/Launcher.UI/Launcher.UI.csproj -c Debug -- `
        --account-api http://127.0.0.1:8766 --account-game --repo $repo --channel internal `
        --install-root (Join-Path $projectRoot 'backend/.local/account-game-install')
} finally { Pop-Location }
