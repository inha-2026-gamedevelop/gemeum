$ErrorActionPreference = 'Stop'
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$pidFile = Join-Path $backendRoot '.local/discord.pid'
if (Test-Path -LiteralPath $pidFile) {
    $existing = Get-Process -Id ([int](Get-Content -LiteralPath $pidFile)) -ErrorAction SilentlyContinue
    if ($existing) { throw 'Discord bot PID is already running.' }
}
$stopFile = Join-Path $backendRoot '.local/discord.stop'
if (Test-Path -LiteralPath $stopFile) { Remove-Item -LiteralPath $stopFile }
$bot = Start-Process -FilePath (Join-Path $backendRoot '.venv/Scripts/python.exe') `
    -ArgumentList '-u', 'scripts/run_discord_bot.py' -WorkingDirectory $backendRoot `
    -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $backendRoot '.local/discord.stdout.log') `
    -RedirectStandardError (Join-Path $backendRoot '.local/discord.stderr.log')
$bot.Id | Set-Content -LiteralPath $pidFile
Start-Sleep -Seconds 3
if ($bot.HasExited) { throw 'Discord bot exited; inspect .local/discord logs.' }
Write-Host 'Discord process started; check .local/discord.stdout.log for READY.'
