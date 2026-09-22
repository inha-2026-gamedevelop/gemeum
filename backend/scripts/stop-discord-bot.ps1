$ErrorActionPreference = 'Stop'
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$pidFile = Join-Path $backendRoot '.local/discord.pid'
if (-not (Test-Path -LiteralPath $pidFile)) { return }
$botPid = [int](Get-Content -LiteralPath $pidFile)
$bot = Get-Process -Id $botPid -ErrorAction SilentlyContinue
if ($bot) {
    $info = Get-CimInstance Win32_Process -Filter "ProcessId = $botPid"
    if ($info.CommandLine -notmatch 'scripts/run_discord_bot\.py') {
        throw 'PID belongs to another process; not stopping.'
    }
    'stop' | Set-Content -LiteralPath (Join-Path $backendRoot '.local/discord.stop')
    if (-not $bot.WaitForExit(15000)) { throw 'Discord shutdown timed out.' }
}
Remove-Item -LiteralPath $pidFile
