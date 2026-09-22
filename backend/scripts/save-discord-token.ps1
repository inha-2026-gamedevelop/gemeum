# Run immediately after copying the Bot token in Developer Portal
# The token is never printed or accepted as a command-line argument
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$envFile = Join-Path $backendRoot '.env'
if (-not (Test-Path -LiteralPath $envFile)) { throw 'Prepare backend/.env first.' }
$tokenValue = [System.Windows.Forms.Clipboard]::GetText().Trim()
if ($tokenValue.Length -lt 50 -or $tokenValue.Length -gt 256 -or $tokenValue -notmatch '^[A-Za-z0-9_.-]+$') {
    throw 'Clipboard does not contain a valid-looking bot token; nothing was saved.'
}
try {
    $lines = @(Get-Content -LiteralPath $envFile -Encoding UTF8 | Where-Object { $_ -notmatch '^GEUMEUM_DISCORD_BOT_TOKEN=' })
    $lines += 'GEUMEUM_DISCORD_BOT_TOKEN=' + $tokenValue
    [IO.File]::WriteAllLines($envFile, $lines, (New-Object Text.UTF8Encoding $false))
    [System.Windows.Forms.Clipboard]::Clear()
    Write-Host 'Discord bot token saved to ignored backend/.env; clipboard cleared.'
} finally { $tokenValue = $null; $lines = $null }
