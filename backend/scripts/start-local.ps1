param([switch]$Background, [switch]$DiscordBot)
$ErrorActionPreference = 'Stop'
if ($DiscordBot -and -not $Background) { throw 'Use -Background with -DiscordBot.' }
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $backendRoot
try {
    uv sync --locked
    if ($LASTEXITCODE -ne 0) { throw 'Python dependency installation failed' }
    uv run python scripts/local_db.py start
    if ($LASTEXITCODE -ne 0) { throw 'Local database startup failed' }
    uv run alembic upgrade head
    if ($LASTEXITCODE -ne 0) { throw 'Database migration failed' }
    if ($Background) {
        $apiPort = [int](uv run python -c 'from geumeum_backend.config import Settings; print(Settings().port)')
        if ($LASTEXITCODE -ne 0) { throw 'Invalid local API configuration' }
        $listener = Get-NetTCPConnection -LocalPort $apiPort -State Listen -ErrorAction SilentlyContinue
        if ($listener) { throw "Port $apiPort is already in use; stop the existing API first" }
        $apiProcess = Start-Process -FilePath (Join-Path $backendRoot '.venv\Scripts\geumeum-api.exe') `
            -WorkingDirectory $backendRoot -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $backendRoot '.local\api.stdout.log') `
            -RedirectStandardError (Join-Path $backendRoot '.local\api.stderr.log')
        $apiProcess.Id | Set-Content (Join-Path $backendRoot '.local\api.pid')
        $ready = $false
        for ($attempt = 0; $attempt -lt 40; $attempt++) {
            if ($apiProcess.HasExited) { throw 'API exited; inspect .local/api.stderr.log' }
            try {
                $health = Invoke-RestMethod "http://127.0.0.1:$apiPort/health/ready" -TimeoutSec 1
                if ($health.status -eq 'ready') { $ready = $true; break }
            } catch { Start-Sleep -Milliseconds 250 }
        }
        if (-not $ready) { throw 'API readiness timed out; inspect .local/api.stderr.log' }
        Write-Host "API ready: http://127.0.0.1:$apiPort/docs"
        if ($DiscordBot) { & (Join-Path $PSScriptRoot 'start-discord-bot.ps1') }
    } else {
        uv run geumeum-api
    }
} finally { Pop-Location }
