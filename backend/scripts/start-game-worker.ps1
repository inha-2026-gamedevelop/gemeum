$ErrorActionPreference = 'Stop'
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$pidFile = Join-Path $backendRoot '.local/worker.pid'
if (Test-Path -LiteralPath $pidFile) {
    $existing = Get-Process -Id ([int](Get-Content -LiteralPath $pidFile)) -ErrorAction SilentlyContinue
    if ($existing) { throw 'A worker PID is already recorded. Run stop-game-worker.ps1 first.' }
}
$stopFile = Join-Path $backendRoot '.local/worker.stop'
if (Test-Path -LiteralPath $stopFile) { Remove-Item -LiteralPath $stopFile }
$worker = Start-Process -FilePath (Join-Path $backendRoot '.venv/Scripts/geumeum-game-worker.exe') `
    -WorkingDirectory $backendRoot -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $backendRoot '.local/worker.stdout.log') `
    -RedirectStandardError (Join-Path $backendRoot '.local/worker.stderr.log')
$worker.Id | Set-Content -LiteralPath $pidFile
Start-Sleep -Seconds 2
if ($worker.HasExited) { throw 'Game worker exited; inspect .local/worker.stderr.log' }
Write-Host 'Local game worker started.'
