param([switch]$Database)
$ErrorActionPreference = 'Stop'
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $PSScriptRoot 'stop-discord-bot.ps1')
& (Join-Path $PSScriptRoot 'stop-game-worker.ps1')
$pidFile = Join-Path $backendRoot '.local\api.pid'
if (Test-Path -LiteralPath $pidFile) {
    $apiId = [int](Get-Content -LiteralPath $pidFile)
    $apiProcess = Get-Process -Id $apiId -ErrorAction SilentlyContinue
    # PID may have been recycled; never stop an unrelated process
    if ($apiProcess) {
        $expected = Join-Path $backendRoot '.venv\Scripts\geumeum-api.exe'
        if ($apiProcess.Path -ne $expected) { throw 'PID belongs to a different process; not stopping' }
        # Python's Windows entry-point launcher has child processes. Validate the
        # full owned tree before stopping any member, avoiding orphan listeners
        $tree = [System.Collections.Generic.List[object]]::new()
        $pending = [System.Collections.Generic.Queue[int]]::new()
        $pending.Enqueue($apiId)
        while ($pending.Count -gt 0) {
            $parentId = $pending.Dequeue()
            $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$parentId")
            foreach ($child in $children) {
                if ($child.ExecutablePath -eq (Join-Path $env:WINDIR 'System32\conhost.exe')) {
                    continue
                }
                if ($child.CommandLine -notlike "*$expected*") {
                    throw 'Unexpected child process; not stopping the process tree'
                }
                $tree.Add($child)
                $pending.Enqueue([int]$child.ProcessId)
            }
        }
        for ($index = $tree.Count - 1; $index -ge 0; $index--) {
            $child = $tree[$index]
            $current = Get-CimInstance Win32_Process -Filter "ProcessId=$($child.ProcessId)"
            if ($current -and $current.CreationDate -eq $child.CreationDate) {
                Stop-Process -Id $child.ProcessId -ErrorAction SilentlyContinue
            }
        }
        Stop-Process -Id $apiId -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $pidFile
}
if ($Database) {
    Push-Location $backendRoot
    try { uv run python scripts/local_db.py stop }
    finally { Pop-Location }
}
