$ErrorActionPreference = 'Stop'
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$pidFile = Join-Path $backendRoot '.local/worker.pid'
if (-not (Test-Path -LiteralPath $pidFile)) { return }
$worker = Get-Process -Id ([int](Get-Content -LiteralPath $pidFile)) -ErrorAction SilentlyContinue
if ($worker) {
    if ($worker.Path -ne (Join-Path $backendRoot '.venv/Scripts/geumeum-game-worker.exe')) {
        throw 'PID belongs to another process; not stopping.'
    }
    'stop' | Set-Content -LiteralPath (Join-Path $backendRoot '.local/worker.stop')
    if (-not $worker.WaitForExit(30000)) { throw 'Worker shutdown timed out; inspect its log.' }
}
Remove-Item -LiteralPath $pidFile
